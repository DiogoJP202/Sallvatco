<!-- markdownlint-disable MD013 -->

# Sallvat & Co

Sallvat & Co. é o projeto de um e-commerce para uma marca de perfumes artesanais. A aplicação deverá unir uma experiência institucional de marca a uma operação completa de catálogo, estoque, carrinho, checkout, pagamento, frete e administração.

## Estado atual

O planejamento da Fase 0 está concluído e a **Fase 1 — Fundação técnica** foi iniciada com a solution e os projetos-base. Ainda não existem banco, migrations, containers ou páginas. A documentação em [`docs/`](docs/README.md) é a fonte de verdade do desenvolvimento.

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

Restaure, compile e teste:

```powershell
dotnet tool restore
dotnet restore Sallvat.sln --locked-mode
dotnet build Sallvat.sln --no-restore
dotnet test Sallvat.sln --no-build
```

Warnings e analyzers são tratados como erros pelo build. Formatação e estilos básicos são definidos no `.editorconfig`, versões NuGet são centralizadas e cada projeto possui lock file reproduzível.

## Migrations

Ainda não existe migration: um schema vazio não justifica histórico. Quando a primeira entidade persistida for aprovada, use a ferramenta local fixada no repositório:

```powershell
dotnet ef migrations add InitialSchema `
  --project src/Sallvat.Infrastructure `
  --startup-project src/Sallvat.Web `
  --output-dir Persistence/Migrations

dotnet ef database update `
  --project src/Sallvat.Infrastructure `
  --startup-project src/Sallvat.Web
```

Os projetos-base respeitam as dependências registradas e ainda não antecipam funcionalidades. Migrations, container Web, Tailwind e páginas serão adicionados nas tarefas correspondentes do [backlog](docs/BACKLOG.md).
