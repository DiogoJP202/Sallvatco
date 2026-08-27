<!-- markdownlint-disable MD013 MD060 -->

# Requisitos

## Requisitos funcionais

| ID | Requisito | MVP |
|---|---|---|
| RF-001 | Exibir home institucional com conteúdo administrável ou configurável. | Sim |
| RF-002 | Listar apenas produtos e variantes ativos, com paginação e filtros essenciais. | Sim |
| RF-003 | Exibir produto por slug com imagens, descrição, notas olfativas e variantes. | Sim |
| RF-004 | Administrar produtos, variantes, imagens, destaques e status de publicação. | Sim |
| RF-005 | Controlar estoque por variante e manter histórico de movimentos. | Sim |
| RF-006 | Manter carrinho de visitante por identificador aleatório em cookie e mesclá-lo após login. | Sim |
| RF-007 | Recalcular preço, cupom, estoque e frete no servidor antes de criar o pedido. | Sim |
| RF-008 | Aplicar cupom elegível e registrar seu uso sem permitir aplicação duplicada. | Sim |
| RF-009 | Calcular frete por CEP, peso e dimensões e armazenar a opção escolhida. | Sim |
| RF-010 | Permitir checkout sem criação obrigatória de conta. | Sim |
| RF-011 | Criar pedido com snapshots de itens, comprador, endereço, frete e totais. | Sim |
| RF-012 | Redirecionar o comprador ao Mercado Pago Checkout Pro. | Sim |
| RF-013 | Confirmar pagamento por webhook validado e consulta ao provedor. | Sim |
| RF-014 | Tratar notificações e comandos externos de forma idempotente. | Sim |
| RF-015 | Permitir cadastro, confirmação de e-mail, login, logout e recuperação de senha. | Sim |
| RF-016 | Permitir que cliente autenticado gerencie endereços e consulte pedidos vinculados. | Sim |
| RF-017 | Permitir operação administrativa de pedido, preparação, envio, rastreio e reembolso. | Sim |
| RF-018 | Auditar alterações administrativas relevantes com ator, alvo, horário e correlação. | Sim |
| RF-019 | Gerar sitemap, robots, canonical, Open Graph e dados estruturados válidos. | Sim |
| RF-020 | Receber, validar, processar e armazenar imagens sem gravá-las no PostgreSQL. | Sim |

## Requisitos não funcionais

| ID | Requisito |
|---|---|
| RNF-001 | Executar em .NET 10 LTS, PostgreSQL e Linux com containers. |
| RNF-002 | Começar como monólito modular administrável por um único desenvolvedor. |
| RNF-003 | Não expor PostgreSQL nem Kestrel diretamente à internet. |
| RNF-004 | Usar HTTPS, HSTS, cookies seguros, antiforgery, CSP, autorização e rate limiting. |
| RNF-005 | Persistir dinheiro sem ponto flutuante e timestamps em UTC. |
| RNF-006 | Evitar overselling por reserva atômica e controle de concorrência. |
| RNF-007 | Não armazenar cartão, CVV, senha, token ou segredo em logs ou banco da aplicação, salvo token de integração quando estritamente necessário e protegido. |
| RNF-008 | Manter logs estruturados correlacionáveis e trilha de auditoria separada. |
| RNF-009 | Ter backup externo criptografado, política de retenção e restore testado. |
| RNF-010 | Otimizar imagens, HTML e assets para SEO e Core Web Vitals. |
| RNF-011 | Isolar Development, Staging e Production em configurações, credenciais, bancos e volumes. |
| RNF-012 | Testar regras críticas por comportamento, sem meta artificial de cobertura. |

## Regras conhecidas

- Produto representa uma fragrância; volume é uma variante, não outro produto.
- SKU, preço, dimensões, peso e estoque pertencem à variante.
- O servidor é a autoridade para preço, desconto, frete e totais.
- Pedido e itens são históricos: mudanças futuras de produto não alteram snapshots.
- Retorno do navegador após o pagamento não confirma pagamento.
- Pedido pago não pode ser cancelado sem um fluxo financeiro compatível; normalmente resulta em reembolso.
- Exclusão de produto com histórico é substituída por inativação.
- Guest checkout não cria credencial nem associa pedidos a e-mail não confirmado.
- CPF não será coletado até existir necessidade legal ou operacional aprovada.
- Apenas as roles `Customer` e `Admin` fazem parte do MVP.

## Critérios gerais de aceite

- toda entrada externa é validada no servidor;
- falha externa não deixa pedido, estoque ou pagamento em estado silenciosamente inconsistente;
- operações repetidas produzem o mesmo resultado ou conflito explícito;
- ações administrativas críticas geram auditoria;
- mensagens ao usuário não expõem stack trace, segredo ou dados de terceiros;
- requisitos aplicáveis possuem tarefas no [BACKLOG.md](BACKLOG.md) e cenários no [TESTING.md](TESTING.md).

## PENDING BUSINESS DECISIONS

| ID | Decisão necessária | Padrão técnico enquanto pendente | Bloqueia |
|---|---|---|---|
| PBD-001 | Razão social, CNPJ, endereço, domínio, contatos e responsável LGPD. | Usar placeholders apenas em ambiente não produtivo. | Conteúdo legal e go-live |
| PBD-002 | Identidade visual, tipografia, paleta, textos, fotografias e catálogo inicial. | Não inventar conteúdo de marca. | UI final e catálogo |
| PBD-003 | Existe obrigação fiscal ou logística de coletar CPF? | Não coletar CPF. | Checkout/nota fiscal |
| PBD-004 | Quais meios de pagamento, parcelas, juros e boleto estarão habilitados? | Planejar cartão e Pix pelo Checkout Pro; não prometer boleto. | Homologação de pagamento |
| PBD-005 | Qual o tempo de reserva e como tratar aprovação após expiração? | Configurável, recomendado 30 minutos; divergência vai para `RequiresAttention`. | Política de estoque/pagamento |
| PBD-006 | Regras e prazos de cancelamento, troca, devolução e reembolso parcial. | Suportar reembolso total; não publicar prazos. | Políticas e operação |
| PBD-007 | CEP/endereço de origem, embalagem, prazo de manuseio, transportadoras e regiões atendidas. | Cotação sandbox com dados de teste explícitos. | Frete produtivo |
| PBD-008 | Haverá frete grátis, retirada local ou promoção de frete? | Nenhuma dessas opções ativa. | Checkout/prom promoções |
| PBD-009 | Cupons podem acumular? Quais limites, usos, produtos e relação com frete? | Um cupom por pedido, sem acumulação e sem desconto no frete. | Regras finais de cupom |
| PBD-010 | Qual provedor, domínio remetente e conteúdo dos e-mails transacionais? | Contrato `IEmailSender`, sem provedor selecionado. | Confirmação de conta e comunicação |
| PBD-011 | Como convidado acessará o acompanhamento do pedido? | Link assinado e expirável enviado ao e-mail informado. | UX pós-compra |
| PBD-012 | Prazos de retenção, anonimização e procedimento de atendimento ao titular. | Preservar dados fiscais/contratuais necessários; revisar juridicamente antes do go-live. | LGPD/go-live |
| PBD-013 | Serão usados analytics, pixels, cookies não essenciais e newsletter com double opt-in? | Somente cookies essenciais; nenhum pixel ou newsletter ativo. | Marketing e consentimento |
| PBD-014 | Qual destino externo e retenção definitiva dos backups? | 7 diários, 4 semanais e 6 mensais, criptografados fora da VPS. | Produção |
| PBD-015 | Qual plano da VPS, domínios de staging/produção e configuração da conta Cloudflare? | Topologia definida sem valores de capacidade. | Provisionamento |
| PBD-016 | Quem terá acesso administrativo e como ocorrerá concessão/revogação? | Uma conta inicial criada por processo seguro e individual; sem conta compartilhada. | Operação |
| PBD-017 | Instagram será link ou embed e qual será o canal oficial de suporte? | Link simples; nenhum script/widget externo. | Conteúdo da home/contato |
