<!-- markdownlint-disable MD013 -->

# Sallvat & Co

E-commerce de perfumes artesanais em desenvolvimento: experiência de marca, catálogo, estoque, carrinho, pedidos e administração em um monólito modular ASP.NET Core MVC.

## Estado atual — 06/10/2026

**Vitrine publicada; loja transacional ainda não liberada para produção.** O núcleo do MVP está implementado em boa parte e coberto por testes, mas homologação externa, logística, comunicações, segurança de implantação e decisões comerciais continuam pendentes. Não considerar as fases 1–5 integralmente homologadas.

- [Apresentação no GitHub Pages](https://diogojp202.github.io/Sallvatco/): home, Sobre, quatro perfumes e nove apresentações de body splash, com filtros/galerias/variantes demonstrativos. Sem login, compra ou backend nesse endereço.
- Aplicação MVC: contas, endereços, CRUD de catálogo/imagens/estoque, carrinho, cupons, pedido transacional e reservas. Ainda faltam provedor de e-mail, primeiro Admin seguro e histórico/vínculo guest completos.
- Frete: cotação/revalidação Melhor Envio, desativada. Uma unidade por cotação enquanto a caixa maior não for validada; não há compra de etiqueta ou rastreio implementados.
- Pagamentos: Orders Sandbox, webhook, recuperação e reembolso total auditados; **todas as flags externas permanecem desligadas**. Testes não representam compras reais. Produção do Mercado Pago é recusada pela configuração atual.
- Infraestrutura: CI e Pages operacionais; apenas PostgreSQL de Development em Compose. VPS, imagem da aplicação, proxy, Staging, backups externos e restore operacional ainda não foram entregues.

O [relatório de situação](docs/STATUS.md) consolida entregas, evidências, limitações, todas as pendências comerciais e ordem de continuidade. A documentação não habilita serviços nem aprova go-live.

## Documentação para começar

- [Índice e convenções](docs/README.md).
- [Estado, mapa do código e o que falta](docs/STATUS.md).
- [Desenvolvimento, revisão e commits](docs/DEVELOPMENT.md).
- [Configuração e flags por ambiente](docs/CONFIGURATION.md).
- [Checklist de homologação e operação](docs/OPERATIONS.md).
- [Roadmap](docs/ROADMAP.md), [backlog](docs/BACKLOG.md) e [decisões comerciais](docs/REQUIREMENTS.md#pending-business-decisions).

## Stack e arquitetura

.NET 10, MVC/Razor, Identity Guid, EF Core/Npgsql/PostgreSQL, Tailwind CSS e JavaScript nativo. Serilog em stdout e testes unitários/de integração. Mercado Pago Checkout Pro via Orders como fluxo Sandbox em desenvolvimento; adapter Preferences isolado e mutuamente exclusivo. Melhor Envio para cotação. Docker/Nginx/Cloudflare no VPS são a topologia planejada, não um deploy já disponível.

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
tools/
└── Sallvat.Showcase/
docs/
.github/workflows/
compose.yaml
```

Application depende de Domain; Infrastructure implementa serviços persistentes e adapters; Web usa Application e registra Infrastructure no composition root. Não há microserviços, SPA separada, repositório genérico ou event bus. Ver [arquitetura](docs/ARCHITECTURE.md).

## Desenvolvimento local

Pré-requisitos: SDK conforme [`global.json`](global.json), Node conforme [`.nvmrc`](.nvmrc), npm conforme [`package.json`](package.json) e Docker Compose v2 para PostgreSQL. Os arquivos fixados no repositório prevalecem sobre versões citadas em registros antigos.

Na raiz, criar `.env` **somente se não existir** e definir senha local antes de iniciar o banco:

```powershell
Copy-Item .env.example .env
docker compose up -d postgres
docker compose ps
```

O banco publica apenas `127.0.0.1:5432`. `docker compose down` preserva dados; a opção `--volumes` os apaga e não deve ser usada como limpeza rotineira.

Configurar a mesma senha local fora do Git; o valor abaixo é apenas placeholder:

```powershell
dotnet user-secrets --project src/Sallvat.Web set "ConnectionStrings:SallvatDatabase" "Host=127.0.0.1;Port=5432;Database=sallvat;Username=sallvat;Password=SUBSTITUIR_LOCALMENTE"
npm ci --ignore-scripts
npm run css:build
dotnet tool restore
dotnet restore Sallvat.sln --locked-mode
dotnet build Sallvat.sln -c Release --no-restore
dotnet test Sallvat.sln -c Release --no-build
```

Warnings/analyzers são erros. Dependências possuem lock files. Os testes PostgreSQL exigem servidor **isolado de testes** via `SALLVAT_TEST_POSTGRES`; sem isso, são pulados localmente e executados no CI. Detalhes em [desenvolvimento](docs/DEVELOPMENT.md) e [testes](docs/TESTING.md).

## Migrations e execução

Há 13 migrations versionadas até `AddPaymentRefundDispatch`; startup nunca as executa. Com banco local saudável e conexão apontando para Development, aplicar explicitamente:

```powershell
dotnet ef database update --project src/Sallvat.Infrastructure --startup-project src/Sallvat.Web -- --environment Development
dotnet run --project src/Sallvat.Web
```

Perfil padrão: `http://localhost:5170`; portas usadas em previews anteriores não alteram esse default. Não há conta Admin, senha ou catálogo de produção criados automaticamente. Development guarda e-mails, imagens e chaves em `.local/`, fora do Git; e-mails contêm links temporários e exigem cuidado. Fora de Development o envio de e-mail está indisponível até integrar provedor.

`/health/live` verifica o processo; `/health/ready` verifica PostgreSQL, não todas as integrações/storage. Para alterar views/styles: `npm run css:watch`. Ver opções obrigatórias e limites em [CONFIGURATION.md](docs/CONFIGURATION.md).

Novas migrations usam a ferramenta local e precisam de revisão de SQL, testes e plano de compatibilidade. Nunca editar migrations já aplicadas nem executar downgrade destrutivo para contornar guardas financeiras. Staging/Production exigem backup e etapa explícita de deploy.

## Apresentação estática

```powershell
dotnet run --project tools/Sallvat.Showcase/Sallvat.Showcase.csproj -c Release -- --output .local/showcase-review --site-url https://diogojp202.github.io/Sallvatco/
```

O exportador cria dados descartáveis e verifica rotas/assets. Os perfumes ainda usam preços, notas e disponibilidade demonstrativos; não são seed de produção. A linha corporal é editorial. Imagens tratadas e aprovação comercial estão documentadas em [IMAGES.md](docs/IMAGES.md). Lumiere mantém o slug legado `cumiere`.

## Commits, CI e publicação

O fluxo acordado trabalha em `main`, com revisão do diff, testes, commit e push por etapa, sem force push. Antes de adicionar arquivos, conferir segredos/dados locais e alterações alheias. Procedimento completo em [DEVELOPMENT.md](docs/DEVELOPMENT.md).

O [CI](.github/workflows/ci.yml) verifica dependências, CSS, Markdown, build, testes (inclusive PostgreSQL efêmero) e formatação. Desde 06/10, ele chama o [Pages reutilizável](.github/workflows/pages.yml) **somente após a validação passar**, no mesmo SHA. Pull requests apenas validam; push em `main` ou execução manual do CI em `main` podem publicar. Exportação e deploy aparecem como jobs da execução do CI, não como dois runs independentes. A etapa só termina com validação e publicação verdes. Não existe deploy automatizado do backend.

Próxima frente: preparar Staging e seus pré-requisitos, homologar integrações com contas de teste e completar as pendências de [STATUS.md](docs/STATUS.md). Nenhuma compra real, ativação financeira ou abertura de produção está autorizada implicitamente por um push.
