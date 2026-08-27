<!-- markdownlint-disable MD013 MD060 -->

# LGPD e privacidade

## Princípios

O sistema aplica finalidade, adequação, necessidade, segurança, prevenção, transparência e responsabilização. Este documento é plano técnico e não substitui revisão jurídica. Dados só serão coletados quando necessários a compra, entrega, segurança, atendimento ou obrigação legal.

## Inventário inicial

| Dado | Finalidade | Origem | Observação |
|---|---|---|---|
| Nome | Identificar comprador e destinatário. | Cliente | Necessário ao pedido/entrega. |
| E-mail | Conta, confirmação, pedido e atendimento. | Cliente | Marketing exige consentimento separado. |
| Telefone | Contato de entrega/atendimento quando necessário. | Cliente | Não usar para marketing por padrão. |
| Endereço/CEP | Cotação, entrega e histórico do pedido. | Cliente | Endereço do pedido vira snapshot. |
| CPF | Fiscal/logística somente se comprovado. | Cliente | Não coletar enquanto `PBD-003` estiver pendente. |
| Histórico de pedidos | Execução contratual, suporte e obrigações. | Sistema | Preservar valores e eventos necessários. |
| IDs de pagamento | Conciliação e suporte. | Mercado Pago | Sem cartão ou CVV. |
| IDs/rastreio de frete | Entrega e suporte. | Melhor Envio/transportadora | Expor apenas ao titular/autorizado. |
| IP, user agent e correlation ID | Segurança e diagnóstico. | Requisição | Retenção curta e acesso restrito. |
| Auditoria administrativa | Segurança e responsabilização. | Sistema | Dados antes/depois são minimizados. |
| Preferências de marketing | Prova de consentimento/opt-out. | Titular | Só se marketing for ativado. |

## Bases e transparência

A base legal de cada tratamento deve ser validada pela Sallvat e assessoria antes do go-live. Em geral, execução de contrato cobre compra/entrega, obrigação legal cobre registros exigidos, legítimo interesse pode cobrir segurança após avaliação, e consentimento separado cobre marketing/cookies opcionais quando adotado.

Política de privacidade deve informar controlador, contato, dados, finalidades, bases, operadores, retenção, direitos, transferências e medidas de segurança em linguagem clara. Termos, trocas e entrega não podem contradizer o comportamento do sistema.

## Minimização

- guest checkout pede apenas dados necessários à entrega e comunicação;
- cadastro não exige endereço;
- CPF fica ausente por padrão;
- carrinho não guarda endereço ou preço em cookie;
- payloads externos e logs são reduzidos;
- analytics e pixels permanecem desativados até `PBD-013`;
- formulários distinguem campo obrigatório de opcional e explicam finalidade.

## Cookies e marketing

Cookies essenciais de autenticação, antiforgery, carrinho e segurança são documentados. Cookies de analytics, publicidade, embed social ou newsletter só carregam após decisão, base adequada e mecanismo de preferência. Recusa não impede compra. Consentimento, quando usado, registra versão, finalidade, instante e revogação.

Newsletter exige consentimento destacado, não pré-marcado, política de descadastro e, se aprovado, double opt-in. E-mail transacional não incorpora marketing sem base apropriada.

## Direitos do titular

Processo operacional deve permitir confirmar tratamento, acessar, corrigir, portar quando aplicável, informar compartilhamentos, revogar consentimento e solicitar eliminação/anonimização quando juridicamente possível.

1. receber solicitação em canal oficial de `PBD-001`;
2. verificar identidade proporcionalmente, sem coletar documento excessivo;
3. localizar dados por conta/e-mail e sistemas operadores;
4. avaliar retenção obrigatória e conflitos antifraude;
5. executar correção, exportação ou anonimização;
6. registrar decisão, responsável e prazo;
7. responder ao titular com segurança.

## Exclusão e anonimização

Excluir a conta não apaga pedidos necessários a obrigações. Credencial pode ser removida/bloqueada e perfil anonimizado, substituindo nome, telefone e e-mail quando permitido, mantendo totais e registros fiscais mínimos. Endereços salvos sem vínculo necessário são removidos. Backups envelhecem pela retenção e não são reescritos; restore deve reaplicar lista de anonimizações pendentes.

## Retenção proposta

Valores definitivos dependem de `PBD-012` e revisão jurídica:

- carrinho abandonado: retenção técnica curta;
- tokens de confirmação/reset: até expiração;
- logs operacionais: padrão de 30 dias, salvo incidente;
- webhook payload sanitizado: tempo necessário à conciliação;
- auditoria: período compatível com segurança e responsabilização;
- pedidos/documentos: prazo legal/fiscal aplicável;
- consentimento: enquanto necessário provar concessão e revogação.

## Operadores e transferências

Mercado Pago, Melhor Envio, provedor de e-mail, Hostinger, Cloudflare e futuro storage/analytics devem constar no inventário de operadores. Antes da produção, revisar termos, localização, suboperadores, medidas, retenção e canal de incidente de cada um.

## Incidente com dados

O processo de [SECURITY.md](SECURITY.md#checklist-de-incidente) deve avaliar natureza, titulares, volume, consequências e mitigação. Controlador e responsável LGPD decidem comunicações à ANPD e titulares conforme obrigação aplicável, preservando evidências e prazos.

## Pendências

`PBD-001`, `PBD-003`, `PBD-012` e `PBD-013` bloqueiam a versão final das políticas e o go-live.
