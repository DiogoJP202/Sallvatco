using Sallvat.Application.Carts;

namespace Sallvat.Application.Checkout;

public sealed record CheckoutBuyerInput(
    string Name,
    string Email,
    string Phone);

public sealed record CheckoutDeliveryInput(
    long? SavedAddressId,
    string RecipientName,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string StateCode);

public sealed record CheckoutDraftInput(
    CheckoutBuyerInput Buyer,
    CheckoutDeliveryInput Delivery);

public sealed record CheckoutBuyer(
    string Name,
    string Email,
    string Phone);

public sealed record CheckoutDelivery(
    string RecipientName,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string StateCode,
    string CountryCode = "BR");

public sealed record CheckoutDraft(
    CheckoutBuyer Buyer,
    CheckoutDelivery Delivery);

public sealed record CheckoutSavedAddress(
    long Id,
    string Label,
    string RecipientName,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string StateCode);

public sealed record CheckoutPrefill(
    string? Name,
    string? Email,
    string? Phone,
    IReadOnlyList<CheckoutSavedAddress> Addresses)
{
    public static CheckoutPrefill Empty { get; } =
        new(null, null, null, []);
}

public sealed record CheckoutValidationError(
    string Field,
    string Message);

public sealed record CheckoutValidationResult(
    CheckoutDraft? Draft,
    CartSummary Cart,
    IReadOnlyList<CheckoutValidationError> Errors)
{
    public bool Succeeded => Draft is not null && Errors.Count == 0;

    public static CheckoutValidationResult Success(
        CheckoutDraft draft,
        CartSummary cart) => new(draft, cart, []);

    public static CheckoutValidationResult Failure(
        CartSummary cart,
        params CheckoutValidationError[] errors) =>
        new(null, cart, errors);
}
