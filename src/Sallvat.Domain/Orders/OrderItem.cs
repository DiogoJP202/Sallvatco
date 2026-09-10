using Sallvat.Domain.Catalog;

namespace Sallvat.Domain.Orders;

public sealed class OrderItem
{
    public const int VariantNameMaxLength = 80;

    private OrderItem()
    {
    }

    public OrderItem(
        long orderId,
        long productVariantId,
        string productName,
        string variantName,
        string sku,
        int quantity,
        decimal unitPrice,
        decimal discountAmount,
        string currency = "BRL")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productVariantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
        ArgumentOutOfRangeException.ThrowIfNegative(discountAmount);

        OrderId = orderId;
        ProductVariantId = productVariantId;
        ProductName = Required(
            productName,
            Product.NameMaxLength,
            nameof(productName));
        VariantName = Required(
            variantName,
            VariantNameMaxLength,
            nameof(variantName));
        Sku = Required(sku, ProductVariant.SkuMaxLength, nameof(sku));
        Quantity = quantity;
        UnitPrice = Money(unitPrice);
        DiscountAmount = Money(discountAmount);
        var gross = UnitPrice * Quantity;
        if (DiscountAmount > gross)
        {
            throw new ArgumentOutOfRangeException(nameof(discountAmount));
        }

        Subtotal = Money(gross - DiscountAmount);
        Currency = Required(
            currency,
            Order.CurrencyLength,
            nameof(currency)).ToUpperInvariant();
        if (Currency != "BRL")
        {
            throw new ArgumentException(
                "Only BRL is supported in the MVP.",
                nameof(currency));
        }
    }

    public long Id { get; private set; }

    public long OrderId { get; private set; }

    public long ProductVariantId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public string VariantName { get; private set; } = string.Empty;

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal Subtotal { get; private set; }

    public string Currency { get; private set; } = "BRL";

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Required(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName);
    }
}
