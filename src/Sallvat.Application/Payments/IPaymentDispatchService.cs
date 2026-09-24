using Sallvat.Application.Carts;

namespace Sallvat.Application.Payments;

public interface IPaymentDispatchService
{
    Task<PaymentDispatchResult> DispatchAsync(long paymentId, CartOwner owner, CancellationToken cancellationToken = default);
}

public enum PaymentDispatchStatus
{
    Ready,
    Disabled,
    NotFound,
    Invalid,
    Conflict,
    RequiresAttention,
}

public sealed record PaymentDispatchResult(PaymentDispatchStatus Status, Uri? CheckoutUrl = null);
