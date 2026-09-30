namespace Sallvat.Application.Payments;

// Internal administrative use case, not a public endpoint. Actor must be an existing Admin.
public interface IPaymentRecoveryService
{
    Task<PaymentRecoveryResult> RecoverAsync(long paymentId, Guid expectedVersion,
        PaymentRecoveryOperation operation, CancellationToken cancellationToken = default);
}

public enum PaymentRecoveryReason { MissingNotification, StatusCheck }

public sealed record PaymentRecoveryOperation(Guid ActorUserId, PaymentRecoveryReason Reason, string CorrelationId);

public enum PaymentRecoveryResult
{
    Disabled, Invalid, Forbidden, NotFound, NotEligible, Conflict, Unavailable,
    Observed, Confirmed, RequiresAttention, Busy, Interrupted, Refunded,
}
