<!-- markdownlint-disable MD013 MD060 -->

# Stack privada de homologação

## Entrega e limite — 19/10/2026

`deploy/staging/compose.yaml` prepara Nginx → ASP.NET Core → PostgreSQL, sem alterar o Compose de Development. É um **ensaio privado reproduzível**, não um Staging implantado nem autorização de produção. CI executa `tools/smoke-staging.sh` com arquivos, certificado, banco e volumes descartáveis. Não há registry publicado, VPS provisionada, certificado de domínio real, Cloudflare configurado, Admin inicial ou envio de e-mail. Sem alterações de schema ou habilitação financeira.

## Isolamento e execução

| Serviço | Redes | Exposição e persistência |
|---|---|---|
| Nginx | `edge` e `proxy` | Apenas `127.0.0.1:8081` HTTP e `127.0.0.1:8443` HTTPS por padrão. Sem volumes de negócio. |
| Web | `proxy` e `database`, ambas internas | Sem portas publicadas; volumes próprios `keys` e `images`. |
| PostgreSQL | `database`, interna | Sem porta publicada; volume próprio `database`. |

O projeto Compose padrão é `sallvat-staging`; não há `container_name`, volumes externos nem nomes globais fixos. Não reutilizar o nome do projeto, sub-rede ou volumes em Production. A rede do proxy usa `172.29.240.0/28`, Nginx `172.29.240.2` e pool dinâmico `172.29.240.8/29`, que não pode conter o IP fixo do Nginx. Conferir colisões no host e, se necessário, alterar **juntos** `STAGING_PROXY_SUBNET`, `STAGING_PROXY_DYNAMIC_RANGE` e `STAGING_PROXY_IP`, mantendo ambos os ranges dentro da sub-rede. O banco usa outra rede criada pelo Compose. A web não tem egress para Internet nesta etapa; todas as flags de pagamento e Melhor Envio são explicitamente falsas.

Web e Nginx executam não-root, read-only, sem capabilities, com `no-new-privileges` e `/tmp` limitado em memória. PostgreSQL usa o entrypoint oficial, que prepara permissões e reduz o usuário do processo; não aplicar cegamente as mesmas restrições de filesystem/capabilities da web ao banco. Limites de 512 MiB/1 CPU para web e banco, 128 MiB/0,5 CPU para Nginx e rotação de 3 arquivos de 10 MiB por serviço são valores iniciais de ensaio, não dimensionamento aprovado para a VPS.

Nginx 1.30.6-alpine e PostgreSQL 18.6-alpine3.24 estão fixados por digest consultado no registry oficial em 19/10. A aplicação é recebida em `SALLVAT_IMAGE`; fora do smoke, usar digest aprovado, não `latest`. O Compose não verifica assinatura/proveniência nem impede que um operador informe uma tag; essa validação pertence ao próximo incremento de promoção.

## Arquivos privados e permissões

Copiar `deploy/staging/.env.example` para arquivo local ignorado. Ele contém somente host, portas, digest e **caminhos absolutos**, nunca valores de senha. Criar os arquivos via canal protegido no host de homologação, fora do repositório:

| Variável | Arquivo | Consumidor |
|---|---|---|
| `STAGING_APPLICATION_SETTINGS` | JSON com `ConnectionStrings:SallvatDatabase` | Web, montado read-only como `/app/appsettings.Staging.json`. |
| `STAGING_DB_OWNER_PASSWORD_FILE` | Senha exclusiva do bootstrap/owner | PostgreSQL. |
| `STAGING_DB_APP_PASSWORD_FILE` | Senha exclusiva de `sallvat_app` | Inicialização PostgreSQL. |
| `STAGING_TLS_CERTIFICATE_FILE` | Cadeia de certificado PEM válida para o host | Nginx. |
| `STAGING_TLS_KEY_FILE` | Chave privada correspondente | Nginx. |
| `STAGING_ACCESS_PASSWORDS_FILE` | Arquivo `htpasswd`, senha forte, hash compatível | Nginx. |

O JSON deve conter somente a conexão: host `postgres`, banco `sallvat_staging`, usuário **`sallvat_app`**, senha correspondente ao arquivo de runtime. Não colocar tokens externos ou configurações arbitrárias nesse JSON. Environment do Compose define host, ambiente, paths herdados da imagem e flags desligadas; não sobrescrever por arquivos extras não revisados.

Compose local monta secrets a partir de arquivos: **não é um cofre criptografado**. Ownership/mode do host precisam permitir leitura pelo UID da aplicação (consultar imagem, atualmente 1654) ou Nginx 101; usar grupos/ACLs mínimos, diretórios restritos e nunca permissões mundiais em segredos reais. `uid/gid/mode` de secrets baseados em arquivo não substituem esse provisionamento. O smoke usa arquivos 644 apenas porque são fixtures sintéticas descartáveis. Não copiar essa permissão para a VPS. Não imprimir `docker inspect`, JSONs ou logs completos em relatórios públicos.

O bootstrap cria apenas `sallvat_app` com login e sem superuser, criação de banco, roles ou schema. Recebe CONNECT/USAGE e permissões DML em tabelas futuras criadas pelo owner. Não cria tabelas nem aplica migrations. O owner inicial é administrativo, **não** deve ser usado pela aplicação. A etapa futura de migrations deve separar credenciais e conferir grants/default privileges conforme o proprietário que criar os objetos.

Inicializadores rodam somente em volume vazio. Alterar arquivos de senha não altera uma role existente; rotação exige procedimento explícito no banco e atualização coordenada da aplicação. Nunca apagar volumes para resolver divergência de senha. `ready` prova conexão, não schema nem permissões de todas as operações.

## TLS, acesso e proxy confiável

- TLS 1.2/1.3, ticket de sessão desativado, host canônico explícito e rejeição de SNI/Host desconhecidos. Certificado e chave não são gerados automaticamente em ambiente operacional.
- HTTP apenas redireciona ao host HTTPS canônico, sem refletir um Host externo. O destino canônico usa porta 443; as portas locais altas são um transporte de ensaio/túnel, não URLs públicas finais.
- Basic Auth protege todas as páginas e assets HTTPS. Não é login de cliente/Admin e não substitui Cloudflare Access. Health não é publicado: retorna 404 mesmo com autenticação; monitorar pela rede interna.
- `X-Robots-Tag: noindex, nofollow, noarchive`, `nosniff`, `DENY`, referrer `no-referrer` e HSTS de um dia, sem `includeSubDomains`/preload. Indexação proibida não substitui controle de acesso. CSP global ainda exige inventário e homologação do frontend.
- Nginx remove Authorization Basic antes do upstream e sobrescreve X-Forwarded-For/Proto. Não confia em Forwarded, X-Forwarded-Host ou CF-Connecting-IP recebidos do cliente.
- ASP.NET processa somente For/Proto vindos do **IP exato** configurado, no máximo um salto e headers simétricos, antes de logs, autenticação e rate limiting. Fora da stack, `ReverseProxy:Enabled` é falso por ausência de configuração; habilitar sem IP válido falha.
- Não habilitar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, confiar em todas as redes ou aceitar IP de cliente informado sem validação. Ainda não há recuperação do IP original Cloudflare: antes de exposição pública, configurar allowlist oficial na origem, Access e confiança de IP no Nginx. Até lá o limitador por IP pode agrupar usuários atrás do mesmo proxy; não desabilitá-lo para contornar isso.
- Access log Nginx registra apenas status e duração. Error log restrito a crítico pode conter contexto diagnóstico e precisa de acesso/retencão controlados; não publicar logs brutos. Logs da aplicação continuam sanitizados em stdout.

## Validação e operação posterior

Para a primeira VPS sem domínio, usar o [kit privado Ubuntu/SSH](VPS.md). Ele acrescenta execução explícita de SQL somente em banco vazio e artifact de imagem, sem registry. Atualizações de banco persistente, e-mail/Admin, backup externo e exposição pública continuam fora do kit.

Validação estrutural, sem subir ambiente (não exibe os valores resolvidos):

```sh
docker compose --env-file /caminho/privado/staging.env -f deploy/staging/compose.yaml config --quiet
```

Ensaio descartável em host Linux com Docker/Compose, Bash, curl e OpenSSL, a partir da raiz:

```sh
bash tools/smoke-staging.sh sallvat-web:revisao-local
```

O script não aceita env-file operacional. Cria projeto exclusivo, portas loopback aleatórias, certificado de um dia e credenciais sintéticas. Valida Compose/Nginx, TLS com verificação do certificado (sem `-k` nas verificações positivas), Basic Auth, assets, headers, redirects, rejeição de Host/SNI desconhecidos, spoofing, readiness interno, role limitada, zero tabelas automáticas e volumes após recriar web. Remove somente seu projeto/volumes/rede/diretório temporário ao finalizar. Docker precisa ser local ao runner Linux. Falha bloqueia Pages pelo CI.

Para operação futura, validar arquivos/permissões, TLS real, grants/schema e backup antes de iniciar a stack. Não executar `down --volumes` em ambiente persistente. Recriar web mantém volumes; não prova backup externo/restore ou compatibilidade de rollback. O Nginx resolve o serviço via DNS Docker, evitando reter o endereço antigo depois de recriação.

Antes de abrir tráfego: registry/promoção por digest, executor explícito de migrations, roles revisadas, email/Admin, certificado/renovação, firewall/Cloudflare Access/Full strict, IP real confiável, CSP e cookies em fluxos autenticados, backups/restore e recursos da VPS. Production e egress Sandbox serão incrementos próprios. Não liberar portas para `0.0.0.0` apenas porque o smoke passou.

Referências: [proxy no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [secrets Compose](https://docs.docker.com/compose/how-tos/use-secrets/), [TLS Nginx](https://nginx.org/en/docs/http/ngx_http_ssl_module.html), [imagem e smoke](CONTAINERS.md) e [runbook de deploy](DEPLOYMENT.md).
