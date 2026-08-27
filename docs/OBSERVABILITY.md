<!-- markdownlint-disable MD013 MD060 -->

# Observabilidade

## Objetivos

Permitir diagnosticar falhas, acompanhar integrações, operar pedidos e investigar ações administrativas sem expor dados sensíveis. Observabilidade técnica não substitui `AuditLog`, que é trilha de responsabilidade de negócio.

## Logs estruturados

Serilog escreverá JSON em stdout. Docker aplicará rotação e retenção; não haverá arquivo ilimitado dentro do container. Campos comuns:

- timestamp UTC, nível, mensagem e `EventId` estável;
- ambiente, versão/commit e nome do serviço;
- correlation ID e trace ID;
- request method, route template, status e duração;
- usuário/ator por ID interno quando autenticado;
- `OrderId`, `PaymentId`, `ShipmentId` ou ID externo quando relevante;
- provedor, operação, tentativa, duração e resultado;
- tipo da exceção e stack apenas no sink protegido.

Usar route template em vez de URL completa para não registrar tokens/query strings.

## Correlação

- aceitar um correlation ID externo apenas se estiver em formato/tamanho permitido; caso contrário gerar novo;
- devolver `X-Correlation-ID` na resposta;
- propagar o identificador em chamadas HTTP de saída quando permitido;
- incluir correlação em `WebhookEvent` e `AuditLog`;
- exibir um identificador de suporte ao usuário em erro inesperado, nunca stack trace.

## Níveis

| Nível | Uso |
|---|---|
| `Debug` | Diagnóstico local; desativado por padrão em Production. |
| `Information` | Operação concluída, transição relevante e request resumido. |
| `Warning` | Validação externa, retry, conflito, tentativa negada ou degradação recuperável. |
| `Error` | Operação falhou e exige investigação/retry. |
| `Fatal` | Processo não inicializa ou perdeu dependência essencial irrecuperável. |

Eventos de domínio não devem gerar múltiplos logs redundantes em controller, application e infrastructure. Registrar uma conclusão e, em falha, o ponto que possui contexto suficiente.

## Dados proibidos

Nunca registrar senha, hash, cookie, antiforgery token, URL de confirmação/reset, connection string, secret, access/refresh token, assinatura de webhook, cartão, CVV, CPF completo, endereço completo ou corpo integral de checkout.

- e-mail: mascarar ou usar hash estável de finalidade limitada;
- telefone/documento: mostrar apenas últimos dígitos quando necessário;
- CEP/IP: minimizar/mascarar conforme finalidade;
- payload externo: guardar campos allowlisted ou hash, não dump indiscriminado;
- exceção de SDK: sanitizar antes de logar propriedades.

## Integrações e webhooks

Para cada chamada externa, registrar provedor, operação, status HTTP, duração, tentativa, ID externo e categoria de falha. Não registrar headers de autenticação.

Para webhook, registrar recebimento, validação de assinatura, deduplicação, consulta canônica, transição e resultado. Duplicata válida é `Information`; assinatura inválida é `Warning` sem revelar cálculo esperado.

## Jobs

Jobs de expiração, conciliação e rastreio registram nome, execução, lote, processados, ignorados, falhas e duração. Cada item usa transação/idempotência própria para que um erro não descarte o lote. Falhas repetidas produzem alerta operacional; não fazem loop sem backoff.

## Auditoria administrativa

`AuditLog` contém:

- ator, ação e role;
- entidade e chave;
- antes/depois apenas dos campos relevantes e sanitizados;
- justificativa quando exigida;
- IP/correlation ID e timestamp UTC;
- resultado da ação.

Auditar alteração de preço, estoque, publicação, imagem, cupom, pedido, status, endereço pós-compra, envio, reembolso, resolução de divergência e acesso administrativo. Auditoria é append-only para a aplicação; acesso e exportação são restritos.

## Health checks

| Endpoint | Conteúdo | Uso |
|---|---|---|
| `/health/live` | Processo responde, sem consultar dependências. | Restart/orquestração. |
| `/health/ready` | PostgreSQL e storage local disponíveis; integrações externas não bloqueiam readiness por falha momentânea. | Deploy e Nginx. |

Respostas públicas não expõem connection string, caminho, versão de pacote ou detalhes de exceção. Nginx restringe detalhes a rede administrativa.

## Retenção e acesso

- logs de container: padrão inicial de 30 dias com limite de tamanho;
- auditoria, webhooks e histórico: retenções específicas conforme `PBD-012`;
- acesso a logs de Production apenas a administradores técnicos;
- relógio da VPS sincronizado para correlação confiável;
- download/exportação de logs é excepcional e protegido.

MVP usa logs e health checks, sem plataforma central obrigatória. Alertas externos/Sentry só serão adicionados após decisão de custo e privacidade.
