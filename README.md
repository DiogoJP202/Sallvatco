<!-- markdownlint-disable MD013 -->

# Sallvat & Co

Sallvat & Co. é o projeto de um e-commerce para uma marca de perfumes artesanais. A aplicação deverá unir uma experiência institucional de marca a uma operação completa de catálogo, estoque, carrinho, checkout, pagamento, frete e administração.

## Estado atual

O projeto está na **Fase 0 — Descoberta e documentação**. Ainda não existem solution, projetos .NET, banco, migrations, containers ou páginas. A documentação em [`docs/`](docs/README.md) é a fonte de verdade para o desenvolvimento futuro.

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

O sistema começará como um **monólito modular**, sem microserviços, SPA separada, CQRS framework ou event sourcing. A divisão futura da solution será:

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

## Início futuro do desenvolvimento

O desenvolvimento só deve começar depois da aprovação desta documentação e da resolução das decisões comerciais que bloquearem cada fase. A primeira tarefa técnica prevista é criar `Sallvat.sln` e os projetos-base, respeitando as dependências registradas, sem antecipar funcionalidades.
