<!-- markdownlint-disable MD013 -->

# Sallvat & Co

Sallvat & Co. é o projeto de um e-commerce para uma marca de perfumes artesanais. A aplicação deverá unir uma experiência institucional de marca a uma operação completa de catálogo, estoque, carrinho, checkout, pagamento, frete e administração.

## Estado atual

O planejamento da Fase 0 e as **Fases 1 a 3** estão concluídos. Já existem fundação técnica, identidade e clientes, catálogo público, seleção de variantes, estoque auditável, imagens WebP seguras, destaques reais na home, metadados Open Graph e dados estruturados de produto. O Admin cadastra e publica perfumes, variantes, estoque e galeria em `/Admin/Produtos`; visitantes acessam apenas conteúdo publicado em `/perfumes`. O próximo incremento inicia a **Fase 4 — Carrinho e cupons**, mantendo preço e disponibilidade sob autoridade do servidor. Os fluxos de e-mail usam caixa de saída local apenas em Development; o provedor real permanece pendente em `PBD-010`. Vínculo de pedidos guest e provisionamento do primeiro Admin dependem das próximas entidades e decisões comerciais. A documentação em [`docs/`](docs/README.md) é a fonte de verdade do desenvolvimento.

## Stack definida

- .NET 10 LTS e ASP.NET Core 10;
- ASP.NET Core MVC com Razor Views;
- Entity Framework Core, Npgsql e PostgreSQL;
- ASP.NET Core Identity;
- Tailwind CSS e JavaScript apenas onde necessário;
- Mercado Pago Checkout Pro;
- Melhor Envio;
- Docker Compose, Nginx, Ubuntu, Cloudflare e Hostinger VPS;
- Serilog, health checks, Git e GitHub.

## Arquitetura

O sistema começa como um **monólito modular**, sem microserviços, SPA separada, CQRS framework ou event sourcing. A estrutura inicial da solution é:

```text
Sallvat.sln
src/
├── Sallvat.Web/
├── Sallvat.Application/
├── Sallvat.Domain/
└── Sallvat.Infrastructure/
tests/
├── Sallvat.UnitTests/
└── Sallvat.IntegrationTests/
```

As responsabilidades, limites de módulos e dependências permitidas estão em [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Documentação

Comece pelo [índice da documentação](docs/README.md). Os documentos cobrem produto, requisitos, banco de dados, autenticação, pedidos, pagamentos, frete, segurança, LGPD, infraestrutura, deploy, testes, SEO, roadmap, backlog e decisões arquiteturais.

## Desenvolvimento local

Pré-requisitos atuais:

- .NET SDK 10.0.400 ou patch posterior da mesma feature band, conforme [`global.json`](global.json);
- Node.js 24.19 e npm 11.17, conforme [`.nvmrc`](.nvmrc) e [`package.json`](package.json);
- Docker com Compose v2 para executar o PostgreSQL de Development.

Crie o arquivo local de ambiente e inicie o banco:

```powershell
Copy-Item .env.example .env
# Edite SALLVAT_POSTGRES_PASSWORD no arquivo .env.
docker compose up -d postgres
docker compose ps
```

O PostgreSQL fica acessível somente em `127.0.0.1:5432` por padrão. `docker compose down` preserva o volume. `docker compose down --volumes` apaga permanentemente o banco local.

Configure a mesma credencial para a aplicação, sem gravá-la no Git:

```powershell
dotnet user-secrets --project src/Sallvat.Web set `
  "ConnectionStrings:SallvatDatabase" `
  "Host=127.0.0.1;Port=5432;Database=sallvat;Username=sallvat;Password=replace-with-the-same-local-password"
```

Restaure o frontend e compile o CSS:

```powershell
npm ci
npm run css:build
```

Durante ajustes nas views, `npm run css:watch` recompila o asset automaticamente. O build .NET detecta alterações nas fontes Razor e exige que as dependências npm estejam restauradas antes de recompilar o CSS.

Restaure, compile e teste a solution:

```powershell
dotnet tool restore
dotnet restore Sallvat.sln --locked-mode
dotnet build Sallvat.sln --no-restore
dotnet test Sallvat.sln --no-build
```

Warnings e analyzers são tratados como erros pelo build. Formatação e estilos básicos são definidos no `.editorconfig`, versões NuGet são centralizadas e cada projeto possui lock file reproduzível.

Em Development, as chaves de Data Protection são persistidas em `.local/data-protection-keys/development`, fora do web root e do Git. Staging e Production devem fornecer um caminho absoluto montado em volume próprio:

```text
DataProtection__KeysPath=/var/lib/sallvat/data-protection-keys
```

Links de confirmação e recuperação não usam o host recebido na requisição. Cada ambiente deve definir sua origem pública canônica; Development já usa `http://localhost:5170`:

```text
AccountLinks__PublicOrigin=https://dominio-do-ambiente.example
```

Enquanto `PBD-010` não define o provedor transacional, Development grava as mensagens em `.local/emails`. Esses arquivos podem conter links temporários e nunca são versionados ou registrados nos logs. Fora de Development, o envio permanece indisponível de forma explícita.

Inicie a aplicação:

```powershell
dotnet run --project src/Sallvat.Web
```

## Migrations

As migrations `InitialIdentityAndCustomers` e `AddCatalogAndInventory` criam a base de identidade/clientes e a primeira fatia de catálogo, imagens, estoque e auditoria. Elas não são executadas automaticamente no startup. Para criar uma próxima migration, use a ferramenta local fixada no repositório:

```powershell
dotnet ef migrations add NomeDaMudanca `
  --project src/Sallvat.Infrastructure `
  --startup-project src/Sallvat.Web `
  --output-dir Persistence/Migrations

dotnet ef database update `
  --project src/Sallvat.Infrastructure `
  --startup-project src/Sallvat.Web
```

O banco local precisa estar saudável antes de `database update`. Em Staging e Production, migrations serão aplicadas por uma etapa explícita de deploy, com backup prévio, nunca pelo processo Web no startup.

## Diagnóstico local

Com a aplicação em execução, `GET /health/live` confirma que o processo responde e `GET /health/ready` também verifica a conexão com o PostgreSQL. As respostas contêm apenas o estado agregado e o header `X-Correlation-ID`, sem detalhes internos. Logs estruturados são escritos como JSON em stdout.

## Integração contínua

O workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) restaura dependências bloqueadas, audita npm/NuGet, recompila e confere o CSS, valida Markdown e formatação, compila em Release e executa todos os testes. As actions externas estão fixadas por commit SHA.
