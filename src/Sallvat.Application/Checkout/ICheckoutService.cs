using Sallvat.Application.Carts;
using Sallvat.Application.Shipping;

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

    Task<FreightQuoteResult> QuoteFreightAsync(
        CartOwner owner,
        string destinationPostalCode,
        CancellationToken cancellationToken = default);

    Task<FreightSelectionResult> RevalidateFreightAsync(
        CartOwner owner,
        string destinationPostalCode,
        string quoteId,
        decimal expectedPrice,
        CancellationToken cancellationToken = default);
}
