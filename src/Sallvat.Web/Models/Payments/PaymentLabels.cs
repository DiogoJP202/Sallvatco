using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.Web.Models.Payments;

public static class PaymentLabels
{
    public static string RefundReason(PaymentRefundReason value) => value switch
    {
        PaymentRefundReason.CustomerRequest => "Solicitação do cliente",
        PaymentRefundReason.FulfillmentUnavailable => "Impossibilidade de atender o pedido",
        PaymentRefundReason.OperationalCorrection => "Correção operacional",
        _ => "Motivo indisponível",
    };

    public static string RefundPreparation(PaymentRefundPreparationStatus value) => value switch
    {
        PaymentRefundPreparationStatus.Prepared => "Intenção de reembolso total registrada. Nenhum envio ao provedor ou devolução de dinheiro foi realizado.",
        PaymentRefundPreparationStatus.AlreadyPrepared => "Já existe uma intenção registrada para esta captura. Nenhum novo registro ou envio foi realizado.",
        PaymentRefundPreparationStatus.Disabled => "Preparação de reembolso desativada neste ambiente.",
        PaymentRefundPreparationStatus.Conflict => "Os registros mudaram ou outra solicitação está em andamento. Recarregue o detalhe antes de continuar.",
        PaymentRefundPreparationStatus.NotEligible => "Captura não elegível para esta preparação. Mantenha a pendência para conferência; não informe reembolso concluído.",
        _ => "Não foi possível registrar a intenção. Recarregue o detalhe e confira o histórico antes de tentar novamente.",
    };

    public static string RecoveryFollowUp(AdminRecoveryFollowUp reason) => reason switch
    {
        AdminRecoveryFollowUp.AttemptsExhausted => "Limite de três consultas automáticas atingido; conferir manualmente.",
        AdminRecoveryFollowUp.WindowExpired => "Janela de 24 horas da recuperação automática encerrada; conferir manualmente.",
        AdminRecoveryFollowUp.AttemptsExhausted | AdminRecoveryFollowUp.WindowExpired => "Limite de consultas atingido e janela automática encerrada; conferir manualmente.",
        _ => "Sem pendência de limite ou janela automática identificada.",
    };

    public static string ExecutionOutcome(PaymentRecoveryOutcome? value) => value switch
    {
        PaymentRecoveryOutcome.Observed => "Consulta concluída sem nova confirmação financeira.",
        PaymentRecoveryOutcome.Confirmed => "Pagamento confirmado e estoque atualizado na mesma transação.",
        PaymentRecoveryOutcome.RequiresAttention => "Divergência registrada; revisão necessária.",
        PaymentRecoveryOutcome.Conflict => "Conflito: os registros mudaram durante a consulta.",
        PaymentRecoveryOutcome.Unavailable => "Consulta indisponível; nenhuma confirmação aplicada.",
        PaymentRecoveryOutcome.Interrupted => "Execução interrompida; resposta antiga bloqueada.",
        PaymentRecoveryOutcome.Forbidden => "Acesso administrativo revogado; confirmação bloqueada.",
        _ => "Sem resultado neste registro; consulte estado e auditoria legada.",
    };

    public static string RecoveryReason(PaymentRecoveryReason? value) => value switch
    {
        PaymentRecoveryReason.MissingNotification => "Notificação não recebida",
        PaymentRecoveryReason.StatusCheck => "Conferência de situação",
        _ => "Motivo indisponível",
    };

    public static string RecoveryResult(PaymentRecoveryResult? value) => value switch
    {
        PaymentRecoveryResult.Confirmed => "Pagamento confirmado e estoque atualizado com auditoria.",
        PaymentRecoveryResult.Observed => "Consulta concluída sem nova confirmação financeira.",
        PaymentRecoveryResult.RequiresAttention => "Divergência registrada. A tentativa exige revisão; não repita a cobrança.",
        PaymentRecoveryResult.Conflict => "Os registros mudaram durante a operação. Recarregue e confira o estado atual.",
        PaymentRecoveryResult.Unavailable => "Não foi possível concluir. Confira o histórico e o estado atual antes de uma nova consulta; não crie outra cobrança.",
        PaymentRecoveryResult.Disabled => "Recuperação desativada neste ambiente.",
        PaymentRecoveryResult.Busy => "Já existe uma recuperação em andamento. Aguarde o prazo informado e recarregue o detalhe.",
        PaymentRecoveryResult.Interrupted => "Execução interrompida ou substituída após o prazo de segurança. A resposta antiga não pode confirmar pagamentos; confira o estado atual antes de nova consulta.",
        PaymentRecoveryResult.NotEligible => "Esta tentativa não permite recuperação. Revisões e tentativas sem ID exigem tratamento específico.",
        PaymentRecoveryResult.Forbidden => "Acesso administrativo não autorizado na operação.",
        PaymentRecoveryResult.NotFound => "Tentativa não encontrada.",
        PaymentRecoveryResult.Invalid => "Dados inválidos para a operação.",
        _ => "Resultado indisponível para exibição. Consulte a auditoria interna.",
    };

    public static string Status(PaymentStatus value) => value switch
    {
        PaymentStatus.Created => "Criada",
        PaymentStatus.Pending => "Aguardando pagamento",
        PaymentStatus.Approved => "Aprovado",
        PaymentStatus.Rejected => "Recusado",
        PaymentStatus.Cancelled => "Cancelado",
        PaymentStatus.Expired => "Expirado",
        PaymentStatus.Refunded => "Reembolsado",
        PaymentStatus.RequiresAttention => "Requer atenção",
        _ => "Desconhecido",
    };

    public static string Dispatch(PaymentDispatchState value) => value switch
    {
        PaymentDispatchState.NotStarted => "Não iniciado",
        PaymentDispatchState.Sending => "Envio em andamento ou sem conclusão registrada",
        PaymentDispatchState.Completed => "Envio concluído",
        PaymentDispatchState.RequiresAttention => "Envio requer revisão",
        _ => "Desconhecido",
    };

    public static string Reason(PaymentAttentionReason? value) => value switch
    {
        PaymentAttentionReason.PreferenceOutcomeUnknown => "Resultado da preferência desconhecido",
        PaymentAttentionReason.LatePreferenceResponse => "Preferência recebida após o prazo",
        PaymentAttentionReason.OrderOutcomeUnknown => "Resultado do envio Orders desconhecido",
        PaymentAttentionReason.LateOrderResponse => "Resposta Orders recebida fora das condições de pagamento",
        PaymentAttentionReason.CanonicalMismatch => "Divergência na consulta ao provedor",
        PaymentAttentionReason.FinancialReview => "Atividade financeira exige revisão",
        PaymentAttentionReason.LateApproval => "Captura observada fora das condições de confirmação local",
        null => "Sem motivo de revisão registrado",
        _ => "Motivo desconhecido",
    };

    public static string Order(OrderStatus value) => value switch
    {
        OrderStatus.PendingPayment => "Aguardando pagamento",
        OrderStatus.Paid => "Pago",
        OrderStatus.Preparing => "Em preparação",
        OrderStatus.Shipped => "Enviado",
        OrderStatus.Delivered => "Entregue",
        OrderStatus.Cancelled => "Cancelado",
        OrderStatus.Refunded => "Reembolsado",
        OrderStatus.RequiresAttention => "Requer atenção",
        _ => "Desconhecido",
    };

    public static string Receipt(WebhookOutcome value) => value switch
    {
        WebhookOutcome.Observed => "Observado, sem nova confirmação",
        WebhookOutcome.Confirmed => "Confirmação aplicada",
        WebhookOutcome.RequiresAttention => "Encaminhado para revisão",
        _ => "Desconhecido",
    };
}
