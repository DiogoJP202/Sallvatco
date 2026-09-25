using Sallvat.Application.Carts;

namespace Sallvat.Application.Payments;

public interface IPaymentReconciliationService
{
    // Read-only assessment, not a financial transition. No external ID is accepted from the caller.
    Task<PaymentReconciliationResult> InspectAsync(long paymentId, CartOwner owner, CancellationToken cancellationToken = default);
}

public enum PaymentReconciliationStatus
{
    AwaitingPayment,
    RequiresAttention,
    Disabled,
    NotFound,
    Unavailable,
    Conflict,
}

public enum PaymentReconciliationReason
{
    None,
    MissingExternalOrderId,
    LocalMismatch,
    ProviderMismatch,
    ProviderNotFound,
    FinancialActivityObserved,
    LocalReviewRequired,
}

public sealed record PaymentReconciliationResult(
    PaymentReconciliationStatus Status,
    PaymentReconciliationReason Reason = PaymentReconciliationReason.None);
