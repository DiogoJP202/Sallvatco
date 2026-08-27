<!-- markdownlint-disable MD013 MD060 -->

# Roadmap

O roadmap ordena trabalho por dependência e risco. Uma fase pode preparar tarefas da seguinte, mas não é considerada pronta sem seus critérios. Decisões `PBD` devem ser resolvidas antes da fase indicada em [REQUIREMENTS.md](REQUIREMENTS.md#pending-business-decisions).

## Fase 0 — Descoberta e documentação

**Objetivo:** estabelecer fonte de verdade suficiente para implementação consistente.

**Dependências:** briefing inicial e validação das decisões arquiteturais.

**Tarefas:**

- consolidar produto, MVP, pós-MVP e pendências;
- definir camadas, módulos, contratos e dependências;
- modelar dados, estoque, pedidos, pagamento, frete e autenticação;
- documentar segurança, LGPD, infraestrutura, deploy, testes e SEO;
- transformar entregas em backlog e ADRs.

**Entregáveis:** README raiz e todos os documentos deste diretório.

**Critérios de aceite:** links e Mermaid válidos; requisitos rastreáveis; pendências numeradas; nenhuma implementação criada; revisão da Sallvat registrada.

**Riscos:** documentação genérica, contradições e decisões comerciais tratadas como técnicas.

**Definição de pronto:** documentação aprovada, ADRs aceitos e pendências bloqueadoras atribuídas.

## Fase 1 — Fundação técnica

**Objetivo:** produzir esqueleto executável e verificável, sem antecipar funcionalidades.

**Dependências:** Fase 0 aprovada e .NET/Docker disponíveis.

**Tarefas:**

- criar solution, seis projetos e referências permitidas;
- configurar analyzers, nullable, tratamento de erros e options;
- preparar Tailwind e layout mínimo sem design final;
- criar PostgreSQL Development via Compose e `DbContext` inicial;
- configurar migrations explícitas, Serilog, correlation ID e health checks;
- criar testes base, CI e documentação de setup.

**Entregáveis:** aplicação inicial saudável, banco vazio versionado, pipeline e ambiente local reproduzível.

**Critérios de aceite:** build/test passam; dependências proibidas ausentes; banco não é público; erro não expõe stack fora de Development; health checks respondem.

**Riscos:** excesso de abstração, diferença Windows/Linux e secrets acidentalmente versionados.

**Definição de pronto:** novo desenvolvedor inicia o projeto pela documentação, CI reproduz o build e nenhuma funcionalidade de negócio foi improvisada.

## Fase 2 — Identidade e clientes

**Objetivo:** entregar conta segura e base para guest/customer.

**Dependências:** Fase 1 e decisão `PBD-010` para e-mail de homologação.

**Tarefas:**

- configurar Identity com `Guid`, confirmação, lockout e Data Protection;
- implementar cadastro, login, logout, reset e alteração de senha;
- modelar `Customer` e `Address`;
- criar área do cliente e autorização por recurso;
- criar roles e procedimento seguro para primeiro Admin;
- implementar vínculo de guest após e-mail confirmado.

**Entregáveis:** fluxos de identidade e conta homologáveis, sem checkout.

**Critérios de aceite:** não há enumeração de e-mail; lockout/rate limit funcionam; guest não ganha conta; cliente não acessa recurso alheio; Admin é auditável.

**Riscos:** entrega de e-mail, cookies atrás do proxy e associação indevida de histórico.

**Definição de pronto:** testes de segurança passam e fluxos completos funcionam em Staging com provedor de e-mail definido.

## Fase 3 — Catálogo, imagens e estoque

**Objetivo:** publicar e administrar perfumes com variantes e disponibilidade confiável.

**Dependências:** Fase 1; `PBD-002` para conteúdo final.

**Tarefas:**

- implementar entidades/configurações de produto, variante e imagem;
- implementar storage local, processamento WebP e thumbnails;
- criar CRUD Admin com validação, concorrência e auditoria;
- criar movimentos e ajustes de estoque;
- criar catálogo, filtros essenciais e página por slug;
- adicionar metadata/JSON-LD inicial e imagens responsivas.

**Entregáveis:** catálogo público e administração completa do catálogo.

**Critérios de aceite:** variante inativa/sem estoque é tratada corretamente; SKU/slug únicos; upload malicioso rejeitado; mudança de preço/estoque auditada; produto inativo não é indexado.

**Riscos:** qualidade/dimensão de imagens, conflito de edição e conteúdo insuficiente.

**Definição de pronto:** produto real pode ser cadastrado e publicado com todas as informações necessárias ao frete futuro.

## Fase 4 — Carrinho e cupons

**Objetivo:** manter intenção de compra sem congelar dados comerciais incorretos.

**Dependências:** Fase 3 e regra inicial de `PBD-009`.

**Tarefas:**

- implementar carrinho guest por token seguro e carrinho de cliente;
- adicionar, alterar, remover e mesclar itens;
- recalcular preço e disponibilidade no servidor;
- implementar cupons, limites e CRUD Admin;
- expirar/limpar carrinhos sem dados necessários;
- instrumentar eventos operacionais sem marketing opcional.

**Entregáveis:** carrinho persistente e desconto validado.

**Critérios de aceite:** manipular cookie/HTML não altera preço; mesclagem é determinística; cupom concorrente respeita limites; carrinho expirado não vaza dados.

**Riscos:** corrida no limite de cupom, acúmulo de carrinhos e UX de preço alterado.

**Definição de pronto:** carrinho produz um resumo server-side apto a iniciar checkout, mas ainda não cria pedido.

## Fase 5 — Checkout e criação do pedido

**Objetivo:** transformar carrinho em pedido consistente para guest ou cliente.

**Dependências:** Fases 2–4; decisões `PBD-003`, `PBD-005`, `PBD-006` e dados básicos de entrega.

**Tarefas:**

- criar formulários e validações de comprador/endereço;
- recalcular itens, cupom e total no servidor;
- implementar snapshots de pedido, item e endereço;
- implementar reserva atômica, expiração e liberação;
- criar máquina de estados e comandos administrativos mínimos;
- produzir confirmação segura para guest.

**Entregáveis:** pedido `PendingPayment` válido, ainda sem gateway real.

**Critérios de aceite:** concorrência da última unidade tem um vencedor; falha faz rollback total; pedido histórico não muda com catálogo; guest não exige conta; expiração é idempotente.

**Riscos:** overselling, snapshots incompletos, reserva longa e checkout duplicado.

**Definição de pronto:** caso de uso de criação é transacional, testado e pronto para receber frete/pagamento reais.

## Fase 6 — Frete e Melhor Envio

**Objetivo:** cotar, selecionar e operar envio com snapshots confiáveis.

**Dependências:** Fases 3 e 5; `PBD-007` e `PBD-008`.

**Tarefas:**

- implementar `IFreightService` e adaptador Melhor Envio;
- configurar sandbox/OAuth, timeout, retry e renovação segura;
- implementar embalagem, cotação e cache curto;
- revalidar e persistir opção no pedido;
- preparar criação de envio, etiqueta e rastreio;
- tratar ausência de opção e falhas sem frete zero.

**Entregáveis:** checkout com frete homologado e base operacional de shipment.

**Critérios de aceite:** preço/prazo snapshot; cotação expirada é revalidada; credencial não aparece em logs; sandbox cobre cenários possíveis e limitações estão registradas.

**Riscos:** algoritmo de embalagem, limitações do sandbox, token expirado e mudança de preço.

**Definição de pronto:** endereços e produtos reais de homologação geram opções coerentes, aprovadas pela operação.

## Fase 7 — Pagamento e webhooks

**Objetivo:** cobrar pelo Checkout Pro e confirmar pedidos de forma idempotente.

**Dependências:** Fases 5–6; `PBD-004` e `PBD-005`.

**Tarefas:**

- implementar `IPaymentGateway` e preferência Checkout Pro;
- criar retorno seguro e estados de tentativa;
- implementar webhook assinado, consulta canônica e deduplicação;
- consumir/liberar estoque conforme confirmação/expiração;
- implementar conciliação, reembolso total e `RequiresAttention`;
- homologar cenários aprovados, rejeitados, pendentes, duplicados e tardios.

**Entregáveis:** checkout financeiro completo em sandbox.

**Critérios de aceite:** retorno não confirma; duplicata tem um efeito; valor/referência divergente bloqueia transição; retry usa idempotency key; nenhum dado de cartão é armazenado.

**Riscos:** evento fora de ordem, aprovação tardia, timeout ambíguo e configuração cruzada de ambientes.

**Definição de pronto:** matriz de pagamento passa em Staging e operação consegue reconciliar exceções.

## Fase 8 — Operação, rastreamento e e-mails

**Objetivo:** permitir fulfillment e comunicação do pedido até a entrega.

**Dependências:** Fases 2, 6 e 7; `PBD-010`, `PBD-011` e políticas de `PBD-006`.

**Tarefas:**

- criar dashboard e filas de pedidos por estado;
- implementar preparação, etiqueta, postagem e rastreio;
- enviar confirmações transacionais idempotentes;
- permitir link guest assinado e histórico autenticado;
- implementar reembolso/retorno operacional aprovado;
- criar jobs de rastreio e conciliação com backoff.

**Entregáveis:** fluxo operacional do pago ao entregue e suporte a exceções.

**Critérios de aceite:** comandos inválidos são bloqueados; rastreio não regride terminal; e-mail duplicado é evitado; falha de e-mail não desfaz pedido; toda alteração sensível é auditada.

**Riscos:** falha durável de comunicação, etiqueta duplicada e política comercial incompleta.

**Definição de pronto:** um pedido de homologação percorre toda a operação e pode ser investigado por logs/auditoria.

## Fase 9 — Segurança, LGPD e hardening

**Objetivo:** comprovar controles antes de produção.

**Dependências:** MVP funcional; `PBD-001`, `PBD-003`, `PBD-012`, `PBD-016`.

**Tarefas:**

- revisar headers, CSP, cookies, antiforgery, autorização e rate limits;
- revisar uploads, secrets, webhooks e logs;
- implementar/exportar/anonimizar dados conforme política;
- validar dependências, imagem e configuração de proxy;
- executar testes de abuso e concorrência;
- finalizar políticas e runbook de incidente.

**Entregáveis:** checklist de segurança/LGPD com evidências e riscos aceitos.

**Critérios de aceite:** nenhum achado crítico/alto aberto; logs sem dados proibidos; direitos têm procedimento; conta Admin e secrets têm ciclo documentado.

**Riscos:** falso senso de segurança, política divergente do sistema e dependência vulnerável.

**Definição de pronto:** revisão assinada pelos responsáveis técnico e de negócio, com correções verificadas.

## Fase 10 — Staging, deploy e go-live

**Objetivo:** colocar o MVP em produção com rollback e restore praticáveis.

**Dependências:** Fases 1–9 e todas as decisões de go-live listadas em [DEPLOYMENT.md](DEPLOYMENT.md#critérios-de-go-live).

**Tarefas:**

- provisionar VPS, Cloudflare, Nginx, redes, volumes e secrets;
- isolar Staging/Production e aplicar recursos;
- configurar CI/image registry e processo de promoção;
- configurar backups externos e alertas mínimos;
- executar restore, rollback e homologação completa;
- realizar compra produtiva controlada e abrir tráfego.

**Entregáveis:** produção operacional, release registrada e runbooks disponíveis.

**Critérios de aceite:** apenas 80/443 públicos; TLS strict; banco não exposto; health/smoke aprovados; backup externo/restore válidos; políticas e suporte publicados.

**Riscos:** capacidade insuficiente, DNS/TLS, migration incompatível e ausência de recuperação.

**Definição de pronto:** go-live aprovado, monitorado e reversível dentro dos limites documentados.

## Fase 11 — Pós-MVP

**Objetivo:** evoluir com evidência de uso e retorno, sem comprometer o núcleo.

**Dependências:** produção estável, métricas autorizadas e prioridades de negócio.

**Possibilidades:** avaliações verificadas, analytics consentido, carrinho abandonado, relatórios, fidelidade, busca avançada, frete alternativo, R2/S3, melhoria de cupons e checkout transparente.

Cada iniciativa exige story própria, revisão LGPD/segurança, ADR quando arquitetural e métricas de sucesso aprovadas.

**Critérios de aceite:** nenhuma iniciativa é iniciada apenas por constar nesta lista; deve haver objetivo, responsável, custo, risco e critério mensurável.

**Riscos:** acumular complexidade, scripts de marketing invasivos e antecipar escala inexistente.

**Definição de pronto:** benefício observado, operação documentada e núcleo do MVP permanece estável.
