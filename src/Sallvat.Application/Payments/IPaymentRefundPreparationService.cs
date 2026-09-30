using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

public interface IPaymentRefundPreparationService
{
    Task<PaymentRefundPreparationResult> PrepareAsync(long paymentId, Guid expectedPaymentVersion, Guid expectedOrderVersion,
        PaymentRefundOperation operation, CancellationToken cancellationToken = default);
}

public sealed record PaymentRefundOperation(Guid ActorUserId, PaymentRefundReason Reason, string CorrelationId);
public enum PaymentRefundPreparationStatus { Prepared, AlreadyPrepared, Disabled, Forbidden, NotFound, Invalid, NotEligible, Conflict, Unavailable }
public sealed record PaymentRefundPreparationResult(PaymentRefundPreparationStatus Status, Guid? RequestId = null);
public sealed record AdminRefundRequest(Guid Id, PaymentRefundRequestState State, decimal Amount, string Currency,
    PaymentRefundReason Reason, DateTimeOffset CreatedAtUtc)
{
    public Guid Version { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? ConfirmedAtUtc { get; init; }
    public string? ExternalRefundId { get; init; }
}
