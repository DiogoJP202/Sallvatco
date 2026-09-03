<!-- markdownlint-disable MD013 MD060 -->

# Architecture Decision Records

Registros abaixo têm status **Aceita** em 2026-08-27. Mudança relevante cria novo ADR que substitui o anterior; histórico não é apagado.

## ADR-001 — ASP.NET Core MVC com Razor Views

**Contexto:** o produto é majoritariamente conteúdo, catálogo, formulários e administração, mantido por um desenvolvedor.

**Decisão:** usar .NET 10 LTS, ASP.NET Core MVC e Razor Views; JavaScript apenas para melhoria progressiva.

**Alternativas consideradas:** SPA React separada; API + frontend independente; Blazor.

**Consequências:** um deploy e autenticação simples, bom SEO e menor superfície. Interações muito dinâmicas exigirão componentes pontuais, sem justificar SPA no MVP.

## ADR-002 — Monólito modular

**Contexto:** catálogo, checkout e operação compartilham transações, equipe e ciclo de deploy.

**Decisão:** um processo e um banco, organizados em módulos funcionais com limites explícitos.

**Alternativas consideradas:** microserviços; monólito sem módulos.

**Consequências:** menor custo e consistência transacional. Disciplina de dependências é necessária para evitar acoplamento; extração futura exige evidência.

## ADR-003 — Quatro projetos de produção

**Contexto:** é preciso separar domínio, casos de uso, infraestrutura e web sem multiplicar assemblies por módulo.

**Decisão:** `Domain`, `Application`, `Infrastructure` e `Web`, com dependências de [ARCHITECTURE.md](ARCHITECTURE.md#dependências).

**Alternativas consideradas:** um único projeto MVC; projeto por módulo/camada; Clean Architecture com mais projetos.

**Consequências:** fronteiras testáveis com complexidade moderada. `Web` referencia `Infrastructure` apenas para composição. Não haverá repositório genérico ou CQRS framework.

## ADR-004 — PostgreSQL com EF Core/Npgsql

**Contexto:** o sistema requer transações, constraints, índices, JSON pontual e baixo custo em Linux.

**Decisão:** PostgreSQL, EF Core e Npgsql; dinheiro em `numeric`, tempo em `timestamptz` e migrations explícitas.

**Alternativas consideradas:** SQL Server; MySQL; SQLite.

**Consequências:** boa integração com VPS/Linux e concorrência robusta. A equipe deve conhecer operação/backup PostgreSQL; funcionalidades específicas ficam confinadas à infraestrutura.

## ADR-005 — Tailwind CSS

**Contexto:** a marca exige identidade visual própria e a UI é server-rendered.

**Decisão:** Tailwind CSS compilado no build, com design tokens e componentes Razor reutilizáveis.

**Alternativas consideradas:** Bootstrap; CSS manual sem framework.

**Consequências:** flexibilidade e bundle purgado, ao custo de uma etapa Node/build e necessidade de padrões para evitar classes inconsistentes.

## ADR-006 — ASP.NET Core Identity e guest checkout

**Contexto:** contas são úteis, mas não devem ser barreira de compra nem ser criadas sem consentimento.

**Decisão:** Identity com `Guid`, `Customer` separado e `ApplicationUserId` opcional. Guest checkout cria cliente/pedido sem credencial; vínculo exige e-mail confirmado.

**Alternativas consideradas:** cadastro obrigatório; autenticação própria; usar IdentityUser como cliente completo.

**Consequências:** menor fricção e melhor separação de dados. Associação de histórico e acesso guest exigem fluxos seguros adicionais.

## ADR-007 — Estoque por variante com reserva

**Contexto:** Checkout Pro redireciona o cliente e confirma de forma assíncrona, criando intervalo sujeito a concorrência.

**Decisão:** manter `OnHand` e `Reserved` por variante; reservar atomicamente no pedido, consumir ao aprovar e liberar ao expirar/cancelar. Movimentos formam histórico.

**Alternativas consideradas:** decrementar apenas após pagamento; decrementar definitivamente ao criar pedido; lock pessimista longo.

**Consequências:** reduz overselling sem bloquear transações durante pagamento. Requer job de expiração e tratamento explícito de aprovação tardia.

## ADR-008 — Mercado Pago Checkout Pro

**Contexto:** o MVP precisa de pagamento online com baixa exposição a dados de cartão.

**Decisão:** Checkout Pro, preferência server-side, redirecionamento, webhook assinado, consulta canônica e idempotência. `IPaymentGateway` isola o domínio.

**Alternativas consideradas:** Checkout Transparente; outro gateway; transferência manual.

**Consequências:** integração e escopo PCI menores, com UX externa e confirmação assíncrona. Retorno do navegador nunca é autoritativo.

## ADR-009 — Melhor Envio atrás de IFreightService

**Contexto:** é necessário cotar e operar múltiplas transportadoras, preservando opção futura de Correios direto ou retirada.

**Decisão:** Melhor Envio no MVP por adaptador específico de `IFreightService`, com snapshot da cotação.

**Alternativas consideradas:** Correios direto; cálculo manual; acoplamento do domínio ao SDK/API.

**Consequências:** cobertura inicial com uma integração e baixo acoplamento. Sandbox limitado, OAuth e embalagem exigem homologação operacional.

## ADR-010 — Imagens em volume local com abstração

**Contexto:** guardar binários no PostgreSQL aumenta backup/custo, enquanto S3/R2 adiciona serviço ao MVP.

**Decisão:** volume persistente local fora do web root e `IImageStorage`; banco guarda chaves/metadados. SkiaSharp `4.151.1` (MIT) decodifica JPEG/PNG/WebP, remove metadados ao recodificar e produz original, versão grande e thumbnail em WebP.

**Alternativas consideradas:** bytea no PostgreSQL; R2/S3 desde o início; URLs de terceiros.

**Consequências:** operação inicial simples e barata, com biblioteca nativa também empacotada para Linux. Backup deve coordenar banco/arquivos e uma única VPS limita disponibilidade; migração futura preserva chaves.

## ADR-011 — Docker Compose, Nginx e Cloudflare na Hostinger

**Contexto:** a produção será uma VPS Ubuntu e precisa de deploy repetível, TLS e isolamento de rede.

**Decisão:** Cloudflare na borda, Nginx como único ingresso, web e PostgreSQL em Compose/redes internas.

**Alternativas consideradas:** instalar .NET/PostgreSQL diretamente; publicar Kestrel; plataforma gerenciada.

**Consequências:** ambiente reproduzível e banco não exposto. A equipe assume patching, monitoramento, volumes e backup da VPS.

## ADR-012 — Staging isolado no mesmo VPS

**Contexto:** pagamentos/frete precisam de homologação semelhante à produção, mas uma segunda VPS aumenta custo.

**Decisão:** Staging no mesmo host, com domínio protegido, containers, banco, volumes, cookies e secrets separados.

**Alternativas consideradas:** sem Staging permanente; VPS separada.

**Consequências:** baixo custo e boa fidelidade de stack. Não testa falha física independente e deve ter limites para não afetar Production.

## ADR-013 — Serilog em stdout e auditoria no banco

**Contexto:** logs técnicos e responsabilidade administrativa têm retenção/consulta diferentes.

**Decisão:** Serilog JSON em stdout com rotação do runtime; `AuditLog` append-only no PostgreSQL para ações de negócio.

**Alternativas consideradas:** arquivos sem estrutura; auditoria apenas em logs; plataforma central obrigatória.

**Consequências:** diagnóstico correlacionável sem serviço adicional. Consultas avançadas/alertas são limitados no MVP e dados devem ser cuidadosamente sanitizados.

## ADR-014 — Deploy imutável, migrations explícitas e backup externo

**Contexto:** auto-migration e tags mutáveis dificultam rollback; backup no mesmo host não cobre perda da VPS.

**Decisão:** imagem por digest/SHA, migration como etapa de deploy, backup prévio e cópia criptografada externa com restore testado.

**Alternativas consideradas:** build na VPS; migration no startup; snapshot da VPS como único backup.

**Consequências:** releases mais previsíveis e recuperáveis, com pipeline/runbook adicional e necessidade de migrations backward-compatible.
