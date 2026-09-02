namespace Sallvat.Domain.Catalog;

public sealed class ProductSlugHistory
{
    private ProductSlugHistory()
    {
    }

    public ProductSlugHistory(
        long productId,
        string slug,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);

        ProductId = productId;
        Slug = CatalogSlug.Normalize(slug);
        CreatedAtUtc = createdAtUtc.Offset == TimeSpan.Zero
            ? createdAtUtc
            : throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public long ProductId { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
