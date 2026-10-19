<!-- markdownlint-disable MD013 MD060 -->

# Primeira homologação na VPS — sem domínio

## Escopo confirmado em 19/10/2026

O primeiro servidor será **homologação**, sem domínio ou Cloudflare por enquanto. Este roteiro mantém a aplicação privada por túnel SSH, com dados sintéticos e todas as integrações desligadas. Não exige registro de domínio, registry, SDK .NET ou Node na VPS. Não configura vendas, e-mail, Admin, backup externo nem catálogo automaticamente.

Base recomendada para reproduzir o CI: **Ubuntu Server 24.04 LTS x86-64/amd64**, Docker Engine atualizado (mínimo 28) e plugin Compose v2. Ubuntu 24.04 tem manutenção padrão até maio de 2029; é uma base suportada, não uma afirmação de ser a versão mais nova. Dimensionamento inicial sugerido para esta homologação: 2 vCPU, 4 GiB RAM e 25–40 GiB SSD, sujeito à capacidade contratada, serviços existentes e observação de consumo. Não contratar capacidade de produção com base apenas nesse valor.

## O que vem do CI

Em uma execução verde de **CI** na `main`, baixar o artifact `sallvat-vps-<SHA COMPLETO>` pela interface do GitHub Actions. Disponibilidade inicial: sete dias. Ele inclui:

- imagem Linux amd64 compactada, construída e testada naquele commit;
- configuração privada Compose/Nginx e scripts de bootstrap existentes;
- `migrations.sql` idempotente das 13 migrations atuais, gerado offline e testado duas vezes em banco descartável;
- `release.json` com revisão, ID imutável da imagem, lista de migrations e checksums dos arquivos;
- `vps.py`, ferramenta de primeira instalação, e este roteiro.

O artifact só é enviado depois dos testes da imagem, da stack e do procedimento de operador. PRs não publicam esse pacote. **Aguardar também Pages verde no mesmo run antes de usar.** O kit pode já existir enquanto o job Pages ainda está em andamento; existência do ZIP não é aceite completo. O workflow não publica em registry nem conecta à VPS. Tokens GitHub, banco, chaves privadas e certificados operacionais não integram o pacote.

Na exportação SQL, um normalizador restrito acrescenta o terminador ausente do comando raw de `AddAutomaticPaymentRecovery`, necessário para o bloco idempotente PostgreSQL. O script falha se esse bloco mudar inesperadamente; não edita migrations originais nem transforma SQL arbitrário. O resultado precisa passar pela aplicação dupla no banco de teste antes de integrar o kit.

Baixar somente do repositório/run/SHA aprovado. Os checksums detectam corrupção e divergência, **não são assinatura independente** contra substituição conjunta dos arquivos e manifesto. Guardar o artifact original e registrar SHA/run na entrega. O kit verifica ID da imagem após importação; a tag local `sallvat-vps:<SHA>` não é prova de integridade sozinha. Upload/download usa o controle de acesso do GitHub; nada requer colocar token no servidor.

## 1. Preparar o servidor, sem afetar outros serviços

1. Confirmar Ubuntu/arquitetura, recursos, console de recuperação do provedor e serviços existentes. Não formatar uma VPS já utilizada nem substituir proxy/firewall de outro projeto.
2. Instalar atualizações do sistema em janela apropriada. Criar usuário administrativo com sudo e chave SSH; testar **uma segunda sessão** antes de restringir senha/root. Não compartilhar chave privada no chat.
3. Instalar Docker Engine e Compose pelo [repositório oficial para Ubuntu](https://docs.docker.com/engine/install/ubuntu/). Não usar script remoto `curl | sh`, desabilitar iptables do Docker ou reinstalar pacotes conflitantes sem revisar o host.
4. Garantir `python3`, `openssl`, `curl` e `unzip`. Usar sudo para Docker nesta fase; grupo `docker` equivale a acesso privilegiado ao host.
5. Na segurança de rede do provedor, permitir SSH apenas da origem administrativa quando viável. **Não abrir 5432, 8080, 8081, 8443, 80 ou 443 para este ensaio.** Não aplicar reset/deny geral antes de confirmar a regra SSH e o console. UFW sozinho não garante bloquear portas publicadas pelo Docker; a stack publica somente loopback.
6. Conferir pelo menos 5 GiB livres após importar os artefatos, portas locais 8081/8443 disponíveis e ausência de conflito com `172.29.240.0/28`. O pré-check recusa rede Docker conflitante, mas também é necessário revisar rotas da VPS/VPN.

Não há script que altere automaticamente SSH, firewall, pacotes do sistema, DNS ou regras Cloudflare.

## 2. Transferir e conferir o pacote

Baixar o ZIP do artifact no computador e transferi-lo por `scp` usando o usuário administrativo. Substituir os nomes em maiúsculas pelos valores reais; eles não são comandos prontos sem adaptação:

```sh
scp sallvat-vps-SHA.zip USUARIO@IP_DA_VPS:~/
```

No servidor, extrair para uma pasta **nova e vazia**, por exemplo `~/sallvat-release-SHA`, e entrar nela. O ZIP do artifact contém `vps.py` e `release.json` na raiz. Não extrair por cima de outra release ou de `/srv/sallvat-staging`.

```sh
python3 vps.py verify
sudo python3 vps.py load
sudo python3 vps.py prepare
sudo python3 vps.py preflight
```

`verify` não altera o servidor. `load` importa a imagem e confere seu ID. `prepare` cria `/srv/sallvat-staging` (700) e seus arquivos privados, recusando diretório/volumes preexistentes. Credenciais aleatórias são geradas no próprio servidor e não aparecem na saída nem nos argumentos de comandos. Secrets de cada consumidor recebem leitura somente pelo UID necessário; não usar `chmod 777` ou 644 para liberar senhas. Certificado público é a exceção deliberada.

O certificado é autossinado, válido por 30 dias, para **`staging.sallvat.test`**. Esse nome é local, não é domínio registrado ou DNS público. O kit fixa o projeto `sallvat-staging`, portas e sub-rede já testados. Se houver conflito, interromper e solicitar ajuste revisado; não editar manifesto, remover checks ou usar outro pacote sobre o estado existente. Sem versão nova do kit, a renovação do certificado pode ser preparada separadamente, preservando permissões e identidade do host.

## 3. Aplicar schema explicitamente, somente no banco vazio

Revisar `migrations.sql` antes de executar. Substituir o SHA pelo campo `revision` de `release.json` (40 caracteres):

```sh
sudo python3 vps.py initialize --confirm-revision SHA_COMPLETO
sudo python3 vps.py start
sudo python3 vps.py check
sudo python3 vps.py status
```

`initialize` exige confirmação, trava contra inicialização paralela, recusa stack em execução e banco com tabelas no schema `public`, inicia somente PostgreSQL e aplica o SQL com parada em erro. Não apaga tabelas, volumes ou pedidos, não executa downgrade e não cria usuário Admin. `start` só libera web/Nginx quando a lista aplicada corresponde à release. As migrations continuam **fora do startup**.

As credenciais administrativas de `sallvat_owner` são usadas exclusivamente nesta inicialização explícita; a web usa `sallvat_app`, sem criação de banco/roles/schema. Este procedimento é para **primeira instalação vazia**, não upgrade. Instalação parcial ou atualização demanda investigação, backup verificável, compatibilidade e plano próprio. Em falha, a ferramenta preserva os arquivos/banco e não tenta desfazer migrations. Nunca apagar `/srv/sallvat-staging` ou usar `down --volumes` como tentativa de reparo.

O catálogo do banco inicial estará vazio, diferentemente dos dados demonstrativos do Pages. E-mail e criação segura do primeiro Admin ainda precisam de incremento próprio; não editar roles/senhas diretamente para contornar isso. `check` prova TLS, acesso Basic, catálogo vazio acessível e bloqueio de health externo, não uma compra ou sessão Admin ponta a ponta.

## 4. Ver no navegador do computador, por SSH

No arquivo hosts **do seu computador**, adicionar:

```text
127.0.0.1 staging.sallvat.test
```

Windows: `C:\Windows\System32\drivers\etc\hosts` (editor com permissão administrativa). Linux/macOS: `/etc/hosts`. Não alterar DNS público.

Manter o túnel abaixo aberto no computador, não na VPS:

```sh
ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:443:127.0.0.1:8443 USUARIO@IP_DA_VPS
```

A porta local 443 precisa estar livre; alguns sistemas exigem permissão elevada para reservá-la. Não parar IIS/proxy/outro serviço sem revisar. O uso de 443 mantém redirects e origem canônica coerentes; não substituir simplesmente por outra porta e concluir que o login está quebrado.

Abrir `https://staging.sallvat.test/`. O navegador avisará sobre o certificado autossinado: antes de aceitar uma exceção **apenas para esse host**, conferir o fingerprint SHA-256 na VPS por canal SSH autenticado:

```sh
sudo openssl x509 -in /srv/sallvat-staging/secrets/tls.crt -noout -fingerprint -sha256
sudo cat /srv/sallvat-staging/secrets/access-login.txt
```

O segundo comando é para o operador consultar a credencial Basic localmente; **não colar sua saída no chat, GitHub ou logs**. Basic Auth é proteção da homologação, não conta Admin/cliente. Não desabilitar validação TLS globalmente nem instalar uma CA desconhecida. Fechar a sessão SSH encerra o acesso pelo túnel; containers seguem rodando na VPS.

## Interromper, diagnóstico e próximos passos

Para parar os serviços preservando dados, a partir da pasta da release:

```sh
sudo docker compose --project-name sallvat-staging --env-file /srv/sallvat-staging/staging.env -f deploy/staging/compose.yaml stop
```

Para retomar a mesma release/schema, usar `start` e `check`. Falha do helper não imprime stderr bruto, pois pode conter SQL/configuração; coletar diagnóstico restrito no host e compartilhar somente mensagem sanitizada. `status` é seguro para verificar estado. Não publicar `docker inspect`, env-files ou logs completos. Não modificar permissões ou executar web como root para “resolver” acesso negado.

Antes de trocar a release ou colocar dados relevantes: planejar backup criptografado externo e restauração, upgrade/rollback e usuário de migrations separado. O kit **não** faz backup/restore e não autoriza atualizações de banco persistente. Antes de acesso público: domínio, certificado confiável/renovação, Cloudflare Access/Full strict e firewall, CSP, IP real confiável, e-mail/Admin e homologação dos provedores. Continuam pendentes as decisões comerciais e o go-live.

Referências oficiais: [suporte Ubuntu](https://ubuntu.com/about/release-cycle), [publicação de portas Docker](https://docs.docker.com/engine/network/port-publishing/), [Docker e firewall](https://docs.docker.com/engine/network/packet-filtering-firewalls/), [scripts de migrations EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
