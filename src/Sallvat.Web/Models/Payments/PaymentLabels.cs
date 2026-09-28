using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.Web.Models.Payments;

public static class PaymentLabels
{
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
