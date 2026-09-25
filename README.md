<!-- markdownlint-disable MD013 -->

# Sallvat & Co

Sallvat & Co. é o projeto de um e-commerce para uma marca de perfumes artesanais. A aplicação deverá unir uma experiência institucional de marca a uma operação completa de catálogo, estoque, carrinho, checkout, pagamento, frete e administração.

## Estado atual

O planejamento da Fase 0 e as **Fases 1 a 5** estão concluídos. A Fase 6 tem a fundação de cotação e as proteções de embalagem implementadas: `/checkout` pode consultar opções no Melhor Envio a partir do CEP e dos dados físicos atuais, mostrar preço/prazo e revalidar a seleção sem aceitar frete zero ou valor enviado pelo navegador. CEP de origem e contato confirmados já estão configurados, com preparo médio de dois dias úteis apresentado separadamente do transporte. Pedidos com mais de uma unidade não recebem cotação automática enquanto a caixa consolidada não for validada; a sacola permanece intacta. A integração fica desabilitada por padrão; credenciais, renovação OAuth e homologação das embalagens ainda estão pendentes. Nenhum pedido ou cobrança é criado pela tela enquanto essas decisões e a Fase 7 não estiverem prontas.

O checkout coleta e normaliza somente contato e entrega, atende guest e cliente, pré-preenche endereços sem alterar a conta e impede acesso a endereço alheio. O caso de uso interno cria `Order`, snapshots comerciais e de frete, consumo de cupom e reservas de estoque na mesma transação, com idempotência por tentativa e proteção contra overselling. A máquina de estados rejeita arestas inválidas; um job cancela pedidos vencidos em lotes e libera estoque e cupom uma única vez. A fila `/Admin/Pedidos` permite sinalizar revisão ou cancelar pedidos ainda sem captura, sempre com versão, justificativa e auditoria. CPF e aceite genérico não são solicitados. A aplicação também mantém sacola guest por token seguro, recalcula preço, estoque e descontos, mescla o conteúdo após login e elimina carrinhos expirados. Os fluxos de e-mail usam caixa de saída local apenas em Development; o provedor real permanece pendente em `PBD-010`. Vínculo de pedidos guest e provisionamento do primeiro Admin dependem das próximas entidades e decisões comerciais. A documentação em [`docs/`](docs/README.md) é a fonte de verdade do desenvolvimento.

A Fase 7 já possui modelo local de tentativas e cliente HTTP isolado para criar preferências Checkout Pro, validado com respostas simuladas. `Payments:MercadoPago` permanece desabilitado e só admite Sandbox nesta etapa. Não há credenciais, páginas de retorno, webhook ou integração do gateway ao checkout. Produção e homologação em PostgreSQL/Mercado Pago continuam pendentes; veja [Pagamentos](docs/PAYMENTS.md).

O fluxo interno de preparação de pagamento também está disponível: valida dono do pedido, snapshots e reservas e persiste uma única tentativa Sandbox, sem HTTP. O CI testa migration e concorrência em PostgreSQL temporário. O dispatcher consome essa tentativa preparada; a integração à tela ainda está pendente. A avaliação de Orders está registrada no [ADR-015](docs/DECISIONS.md#adr-015--preparação-local-independente-da-api-de-checkout).

O gateway também possui contrato Orders separado (`CreateOrderAsync`), com total em string, ID externo próprio e validação de resposta. `IPaymentDispatchService` registra posse exclusiva antes do HTTP, monta valores dos snapshots e persiste ID/resultado independentemente do cancelamento do navegador. A migration `AddPaymentOrderDispatch` inclui constraints e bloqueia rollback se houver envios iniciados. `OrdersEnabled` e `Enabled` (Preferences) são desligados por padrão e não podem ser habilitados juntos. Os testes usam gateway simulado e PostgreSQL isolado no CI; conciliação financeira, webhook e integração à tela ainda estão pendentes. Essa entrega não habilita compras.

O incremento de 25/09 acrescenta consulta canônica Orders e diagnóstico interno de conciliação somente leitura. Confere a resposta contra os snapshots e detecta mudança concorrente; não aprova pagamentos, não altera estoque e não libera reenvio. Tentativas sem ID externo continuam bloqueadas para revisão. Recuperação auditada, webhook, conciliação financeira e homologação permanecem pendentes; as compras continuam desabilitadas.

## Demonstração visual

A apresentação estática é publicada pelo [GitHub Pages](https://diogojp202.github.io/Sallvatco/) a cada atualização da branch `main`. Ela demonstra home, página Sobre, catálogo com quatro fragrâncias, filtros, galerias, variantes, detalhe de produto e uma página de linha corporal com nove produtos. As treze imagens principais foram tratadas com IA a partir dos materiais enviados pela marca, com fundo limpo e enquadramento consistente; não são fotografias originais sem alteração. Rótulos e embalagens precisam de aprovação antes do uso comercial. O [registro das imagens](docs/IMAGES.md) documenta arquivos, limites e prompts. Os dados do catálogo de perfumes continuam descartáveis e são gerados durante o workflow; nomes, textos, preços, estoques e disponibilidade ainda dependem da aprovação comercial. Cadastro, login, carrinho e compra permanecem desabilitados nessa apresentação; a aplicação completa depende do backend ASP.NET Core e será hospedada no VPS.

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

A cotação do Melhor Envio também permanece desligada até que os dados de homologação sejam aprovados. Para um teste sandbox, configure por user-secrets — nunca em `appsettings.json` ou no Git — os valores abaixo e só então altere `Enabled`:

```powershell
dotnet user-secrets --project src/Sallvat.Web set "Shipping:MelhorEnvio:OriginPostalCode" "CEP_DE_ORIGEM"
dotnet user-secrets --project src/Sallvat.Web set "Shipping:MelhorEnvio:AccessToken" "TOKEN_SANDBOX"
dotnet user-secrets --project src/Sallvat.Web set "Shipping:MelhorEnvio:SupportEmail" "EMAIL_DE_SUPORTE"
dotnet user-secrets --project src/Sallvat.Web set "Shipping:MelhorEnvio:Enabled" "true"
```

O ambiente padrão usa `https://sandbox.melhorenvio.com.br/`, timeout de 10 segundos, cache de 120 segundos e cotação válida por 10 minutos. O token estático serve apenas para a homologação técnica inicial; armazenamento e renovação segura do OAuth ainda não estão concluídos.

Inicie a aplicação:

```powershell
dotnet run --project src/Sallvat.Web
```

## Migrations

`AddPaymentFoundation` acrescenta a fundação local de tentativas de pagamento: snapshot comercial, chave idempotente, preferência opcional, revisão de resultado incerto e índices de proteção contra duplicatas. A Fase 7 está apenas iniciada: não há cobrança ou webhook ativo; o adapter Mercado Pago isolado está desabilitado. Consulte [Pagamentos](docs/PAYMENTS.md) para os limites desta entrega e a homologação pendente em PostgreSQL.

As migrations `InitialIdentityAndCustomers`, `AddCatalogAndInventory`, `AddShoppingCarts`, `AddCoupons`, `AddOrdersAndReservations` e `AddOrderLifecycle` criam a base de identidade/clientes, catálogo, imagens, estoque, auditoria, carrinhos, promoções, pedidos, snapshots, reservas e controle de ocorrências. Elas não são executadas automaticamente no startup. Para criar uma próxima migration, use a ferramenta local fixada no repositório:

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

O workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) restaura dependências bloqueadas, audita npm/NuGet, recompila e confere o CSS, valida Markdown e formatação, compila em Release e executa todos os testes. O workflow [`.github/workflows/pages.yml`](.github/workflows/pages.yml) gera uma demonstração estática reproduzível e a publica no GitHub Pages. As actions externas estão fixadas por commit SHA.
