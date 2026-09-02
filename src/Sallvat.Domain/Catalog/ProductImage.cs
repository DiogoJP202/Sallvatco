namespace Sallvat.Domain.Catalog;

public sealed class ProductImage
{
    public const int StorageKeyMaxLength = 500;
    public const int AltTextMaxLength = 200;

    private ProductImage()
    {
    }

    public ProductImage(
        long productId,
        string storageKey,
        string altText,
        int width,
        int height,
        int position,
        bool isCover,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(altText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        var normalizedKey = storageKey.Trim();
        if (normalizedKey.Length > StorageKeyMaxLength
            || normalizedKey.Contains("..", StringComparison.Ordinal)
            || normalizedKey[0] is '/' or '\\')
        {
            throw new ArgumentException(
                "Storage key is invalid.",
                nameof(storageKey));
        }

        var normalizedAltText = altText.Trim();
        if (normalizedAltText.Length > AltTextMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(altText));
        }

        ProductId = productId;
        StorageKey = normalizedKey;
        AltText = normalizedAltText;
        Width = width;
        Height = height;
        Position = position;
        IsCover = isCover;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public long ProductId { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string AltText { get; private set; } = string.Empty;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Position { get; private set; }

    public bool IsCover { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

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
