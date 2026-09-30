using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

public interface IPaymentRefundService
{
    Task<PaymentRefundResult> SendAsync(long paymentId, Guid expectedRefundVersion, PaymentRefundActor actor, CancellationToken cancellationToken = default);
    Task<PaymentRefundResult> CheckAsync(long paymentId, Guid expectedRefundVersion, PaymentRefundActor actor, CancellationToken cancellationToken = default);
}

public sealed record PaymentRefundActor(Guid UserId, string CorrelationId);
public enum PaymentRefundResult { Disabled, Invalid, Forbidden, NotFound, Conflict, NotEligible, Unavailable, AwaitingConfirmation, Confirmed, RequiresAttention }
public sealed record PaymentRefundSubmit(Guid IdempotencyKey, string ExternalOrderId, PaymentEnvironment Environment);
public enum PaymentRefundSubmitResult { Disabled, Invalid, Accepted, Unknown }
public sealed record ConfirmedOrderRefund(string Id, string PaymentId, decimal Amount);
