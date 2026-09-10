using Sallvat.Domain.Carts;

namespace Sallvat.Application.Carts;

public static class CartLimits
{
    public const int MaximumQuantityPerItem = CartItem.MaximumQuantity;
}

public sealed record CartOwner(
    string? GuestToken,
    Guid? ApplicationUserId)
{
    public static CartOwner ForGuest(string token) => new(token, null);

    public static CartOwner ForCustomer(Guid applicationUserId) =>
        new(null, applicationUserId);
}

public sealed record CartImage(
    string Url,
    string AltText,
    int Width,
    int Height);

public sealed record CartLine(
    long ItemId,
    long VariantId,
    string ProductName,
    string ProductSlug,
    int VolumeMl,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal ReferenceUnitPrice,
    string Currency,
    int AvailableQuantity,
    bool IsAvailable,
    CartImage? Image)
{
    public decimal LineTotal => UnitPrice * Quantity;

    public bool PriceChanged => UnitPrice != ReferenceUnitPrice;
}

public sealed record CartSummary(
    IReadOnlyList<CartLine> Items,
    decimal Subtotal,
    string Currency,
    DateTimeOffset? ExpiresAtUtc,
    CartCoupon? Coupon = null)
{
    public int TotalQuantity => Items.Sum(item => item.Quantity);

    public bool CanStartCheckout =>
        Items.Count > 0
        && Items.All(item => item.IsAvailable)
        && (Coupon is null || Coupon.IsValid);

    public decimal DiscountTotal => Coupon?.DiscountTotal ?? 0m;

    public decimal Total => Subtotal - DiscountTotal;

    public static CartSummary Empty { get; } =
        new([], 0m, "BRL", null);
}

public sealed record CartCoupon(
    long Id,
    string Code,
    decimal DiscountTotal,
    bool IsValid,
    string? Message);

public enum CartMutationStatus
{
    Succeeded,
    NotFound,
    Invalid,
    Unavailable,
}

public sealed record CartMutationResult(
    CartMutationStatus Status,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == CartMutationStatus.Succeeded;

    public static CartMutationResult Success() =>
        new(CartMutationStatus.Succeeded, []);

    public static CartMutationResult Failure(
        CartMutationStatus status,
        params string[] errors) =>
        new(status, errors);
}
