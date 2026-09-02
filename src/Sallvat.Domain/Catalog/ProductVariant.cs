namespace Sallvat.Domain.Catalog;

public sealed class ProductVariant
{
    public const int SkuMaxLength = 64;
    public const int CurrencyLength = 3;

    private ProductVariant()
    {
    }

    public ProductVariant(
        long productId,
        string sku,
        int volumeMl,
        decimal price,
        decimal weightKg,
        decimal heightCm,
        decimal widthCm,
        decimal lengthCm,
        DateTimeOffset createdAtUtc,
        string currency = "BRL")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ValidateCommercialData(
            sku,
            volumeMl,
            price,
            weightKg,
            heightCm,
            widthCm,
            lengthCm,
            currency);

        ProductId = productId;
        ApplyCommercialData(
            sku,
            volumeMl,
            price,
            weightKg,
            heightCm,
            widthCm,
            lengthCm,
            currency);
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public long ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string NormalizedSku { get; private set; } = string.Empty;

    public int VolumeMl { get; private set; }

    public decimal Price { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public decimal WeightKg { get; private set; }

    public decimal HeightCm { get; private set; }

    public decimal WidthCm { get; private set; }

    public decimal LengthCm { get; private set; }

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public int Available => OnHand - Reserved;

    public bool IsSellable =>
        IsActive
        && Price > 0
        && WeightKg > 0
        && HeightCm > 0
        && WidthCm > 0
        && LengthCm > 0;

    public void UpdateCommercialData(
        string sku,
        int volumeMl,
        decimal price,
        decimal weightKg,
        decimal heightCm,
        decimal widthCm,
        decimal lengthCm,
        bool isActive,
        DateTimeOffset updatedAtUtc,
        string currency = "BRL")
    {
        ValidateCommercialData(
            sku,
            volumeMl,
            price,
            weightKg,
            heightCm,
            widthCm,
            lengthCm,
            currency);
        ApplyCommercialData(
            sku,
            volumeMl,
            price,
            weightKg,
            heightCm,
            widthCm,
            lengthCm,
            currency);
        IsActive = isActive;
        Touch(updatedAtUtc);
    }

    public int AdjustOnHand(
        int newOnHand,
        DateTimeOffset adjustedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newOnHand);

        if (newOnHand < Reserved)
        {
            throw new InvalidOperationException(
                "On-hand inventory cannot be lower than reserved inventory.");
        }

        var difference = newOnHand - OnHand;
        if (difference == 0)
        {
            throw new InvalidOperationException(
                "Inventory adjustment must change the current balance.");
        }

        OnHand = newOnHand;
        Touch(adjustedAtUtc);

        return difference;
    }

    private void ApplyCommercialData(
        string sku,
        int volumeMl,
        decimal price,
        decimal weightKg,
        decimal heightCm,
        decimal widthCm,
        decimal lengthCm,
        string currency)
    {
        Sku = sku.Trim();
        NormalizedSku = Sku.ToUpperInvariant();
        VolumeMl = volumeMl;
        Price = price;
        WeightKg = weightKg;
        HeightCm = heightCm;
        WidthCm = widthCm;
        LengthCm = lengthCm;
        Currency = currency.Trim().ToUpperInvariant();
    }

    private static void ValidateCommercialData(
        string sku,
        int volumeMl,
        decimal price,
        decimal weightKg,
        decimal heightCm,
        decimal widthCm,
        decimal lengthCm,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        if (sku.Trim().Length > SkuMaxLength
            || sku.Trim().Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "SKU contains invalid characters.",
                nameof(sku));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(volumeMl);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weightKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthCm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lengthCm);

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalizedCurrency = currency.Trim();
        if (normalizedCurrency.Length != CurrencyLength
            || normalizedCurrency.Any(character =>
                !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException(
                "Currency must be an ISO three-letter code.",
                nameof(currency));
        }

        if (!normalizedCurrency.Equals("BRL", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only BRL is supported in the MVP.",
                nameof(currency));
        }
    }

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = RequireUtc(timestamp, nameof(timestamp));
        ConcurrencyVersion = Guid.NewGuid();
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
