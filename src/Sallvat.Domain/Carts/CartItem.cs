namespace Sallvat.Domain.Carts;

public sealed class CartItem
{
    public const int MaximumQuantity = 10;
    public const int CurrencyLength = 3;

    private CartItem()
    {
    }

    public CartItem(
        Guid cartId,
        long productVariantId,
        int quantity,
        decimal referenceUnitPrice,
        DateTimeOffset createdAtUtc,
        string currency = "BRL")
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException(
                "Cart ID cannot be empty.",
                nameof(cartId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productVariantId);
        ValidateQuantity(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(referenceUnitPrice);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency != "BRL")
        {
            throw new ArgumentException(
                "Only BRL is supported in the MVP.",
                nameof(currency));
        }

        CartId = cartId;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        ReferenceUnitPrice = referenceUnitPrice;
        Currency = normalizedCurrency;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public long Id { get; private set; }

    public Guid CartId { get; private set; }

    public long ProductVariantId { get; private set; }

    public int Quantity { get; private set; }

    public decimal ReferenceUnitPrice { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void ChangeQuantity(
        int quantity,
        DateTimeOffset updatedAtUtc)
    {
        ValidateQuantity(quantity);
        Quantity = quantity;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void MoveToCart(
        Guid cartId,
        DateTimeOffset updatedAtUtc)
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException(
                "Cart ID cannot be empty.",
                nameof(cartId));
        }

        CartId = cartId;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > MaximumQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                $"Quantity must be between 1 and {MaximumQuantity}.");
        }
    }

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                parameterName);
        }

        return value;
    }
}
