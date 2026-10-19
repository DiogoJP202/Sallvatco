<!-- markdownlint-disable MD013 MD060 -->

# Imagem da aplicação e smoke isolado

## Escopo — 19/10/2026

Primeiro incremento da preparação de Staging (F10-S2): Dockerfile multistage, contexto restrito, build no CI e smoke de runtime Linux amd64. **Não é um deploy de Staging.** Não publica imagem em registry, configura VPS/domínio/proxy, aplica migrations, cria Admin ou habilita integração externa. Docker não está disponível na estação Windows desta entrega; a execução real do container depende do job Linux do CI. O aceite exige esse job e os jobs Pages verdes no mesmo SHA.

## Construção

Na raiz, em host com Docker para containers Linux:

```sh
docker build --pull --tag sallvat-web:revisao-local .
```

Os três estágios separam responsabilidades:

| Estágio | Base | Conteúdo |
|---|---|---|
| `frontend` | Node 24.19.0 bookworm-slim | `npm ci --ignore-scripts` e compilação Tailwind. |
| `build` | SDK .NET 10.0.400 noble | Restore locked e publish Release framework-dependent. |
| `runtime` | ASP.NET 10.0.11 noble | Somente publish, sem Node, npm, SDK ou ferramentas EF. |

As bases têm digest SHA-256 versionado no Dockerfile e consultado no registry oficial em 19/10. A versão Node acompanha `.nvmrc`, e o SDK acompanha `global.json`. Digest fixa conteúdo, não garante ausência de vulnerabilidades: atualizações exigem revisão, auditoria, build e smoke. Não foi adicionado scanner de vulnerabilidades das camadas do sistema operacional nesta etapa. A publicação futura deve usar registry aprovado, tag por commit e digest final; nunca promover `latest` implicitamente.

O CSS é recompilado no estágio Node e copiado para o estágio SDK. `PrebuiltTailwind=true` é uma opção explícita de build, não flag operacional: omite a segunda compilação Node e exige que o arquivo CSS exista. Builds normais continuam executando o target Tailwind. O smoke compara o CSS servido pela imagem com o asset versionado validado pelo CI, evitando aceitar asset desatualizado apenas porque existe. O estágio SDK também recebe o `.editorconfig` versionado: aplica exatamente as regras existentes, inclusive a classificação de migrations geradas, sem desabilitar analisadores no container.

`.dockerignore` parte de negação total e libera apenas fontes e manifests necessários. Exclui bin/obj, node_modules, `.local`, arquivos de ambiente, certificados/chaves, secrets e configuração Development/Testing/local. Não usar `COPY . .` nem argumentos de build para segredos. Segredo colocado indevidamente em uma fonte permitida ainda seria copiado: revisão de arquivos continua obrigatória.

## Contrato de execução

- Processo `dotnet Sallvat.Web.dll`, usuário não-root `APP_UID` fornecido pela base, HTTP interno 8080; `EXPOSE` não publica porta.
- `/app` contém somente arquivos do publish, sem escrita pela aplicação.
- Diretórios `/var/lib/sallvat/keys` e `/var/lib/sallvat/images` pertencem ao usuário da aplicação e usam permissão 700; montar volumes separados por ambiente.
- Imagem define `DataProtection__KeysPath`, `ImageStorage__RootPath`, porta interna e desabilita diagnósticos remotos do runtime. Não define senha, conexão, domínio ou ambiente Staging implicitamente.
- Operador deverá fornecer `ASPNETCORE_ENVIRONMENT=Staging`, conexão isolada, `AccountLinks__PublicOrigin` HTTPS e `AllowedHosts` restrito. As flags financeiras/frete mantêm os defaults falsos. A imagem não transforma a opção de configuração de e-mail indisponível em provedor funcional.
- Não há startup de migrations ou seed. `/health/ready` verifica **conexão**, não integridade/versão do schema; banco vazio pode estar ready. Deploy precisa verificar migrations separadamente antes de aceitar tráfego.

Volumes novos podem herdar ownership do diretório da imagem. Bind mounts e volumes já existentes precisam de provisionamento de permissões compatíveis com o UID; não resolver erro de acesso executando a aplicação como root ou usando `chmod 777`. Não compartilhar key ring entre ambientes. As chaves XML ainda não recebem criptografia por certificado/KMS: proteger volume, acesso e backups; essa limitação permanece para hardening.

Não executar a imagem diretamente na Internet. Faltam proxy confiável, TLS/headers, Cloudflare Access/noindex de Staging, stacks isoladas e limites ajustados à VPS. O loopback HTTP do smoke é exclusivamente técnico, sem sessão de negócio ou credenciais reais; não comprova cookies/redirects sob HTTPS.

## Smoke reproduzível em Linux

Requer Docker, Bash, curl e acesso aos registries para baixar bases; nenhum token de negócio:

```sh
bash tools/smoke-container.sh sallvat-web:revisao-local
```

O script cria uma rede interna exclusiva, dois containers descartáveis e volumes anônimos. Só a aplicação recebe uma porta aleatória em **127.0.0.1**, nunca em todas as interfaces. PostgreSQL não publica porta. A aplicação roda com filesystem read-only, capabilities removidas, `no-new-privileges`, `/tmp` em tmpfs e limites de memória/CPU/processos. Esses limites são de smoke, não dimensionamento aprovado da VPS.

Verificações:

1. `/health/live` 200 e `/health/ready` 503 sem PostgreSQL.
2. CSS 200 e idêntico ao asset versionado; formulário de login 200.
3. UID não-zero, diretório `/app` sem escrita, ausência de SDK/Node/config Development e dependências nativas SkiaSharp resolvidas por `ldd`.
4. Escrita de uma fixture no volume de imagens, restart, leitura da fixture e hashes do key ring preservados.
5. PostgreSQL descartável disponível: ready 200, sem tabelas criadas pelo startup; banco parado: ready 503 e live 200.

O trap remove apenas containers/volumes/rede criados pelo script e seu diretório temporário. Não passar imagem não confiável: ela será executada. Não apontar o teste a banco operacional nem reutilizar suas credenciais; a senha literal é exclusiva do banco descartável. Logs finais em falha são desse ambiente sintético, não de usuários reais. Restart no mesmo container não substitui ensaio futuro de recriação com volumes operacionais, backup/restore ou rollback.

## CI e próximos incrementos

O job `validate` executa build da imagem por SHA e smoke depois dos testes/lints/auditorias existentes. Sem credenciais de registry e sem `packages: write`. Falha impede Pages através do gate já existente. A imagem é descartada com o runner; a vitrine publicada continua sendo exportação estática, não execução do backend.

Ainda pendentes: registry e promoção por digest; Compose Staging/Production, Nginx/TLS/proxies conhecidos; configuração segura de secrets e volumes; executor explícito de migrations; Admin inicial, e-mail de teste e homologação externa; backups e restore; validação do runtime em outras arquiteturas. A fase 10 e o go-live permanecem abertos em [BACKLOG.md](BACKLOG.md).

Referências: [containers .NET e builds multistage](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container), [imagem oficial ASP.NET](https://hub.docker.com/r/microsoft/dotnet-aspnet) e [procedimento de entrega](DEVELOPMENT.md).
