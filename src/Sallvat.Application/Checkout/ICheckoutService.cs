using Sallvat.Application.Carts;

namespace Sallvat.Application.Checkout;

public interface ICheckoutService
{
    Task<CheckoutPrefill> GetPrefillAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken = default);

    Task<CheckoutValidationResult> ValidateAsync(
        CartOwner owner,
        Guid? applicationUserId,
        CheckoutDraftInput input,
        CancellationToken cancellationToken = default);
}
