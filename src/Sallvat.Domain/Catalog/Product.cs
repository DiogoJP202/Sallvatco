namespace Sallvat.Domain.Catalog;

public sealed class Product
{
    public const int NameMaxLength = 160;
    public const int ShortDescriptionMaxLength = 320;
    public const int DescriptionMaxLength = 6_000;
    public const int AttributeMaxLength = 500;
    public const int ClassificationMaxLength = 100;

    private Product()
    {
    }

    public Product(
        string name,
        string slug,
        string? shortDescription,
        string? description,
        string? olfactoryFamily,
        string? topNotes,
        string? heartNotes,
        string? baseNotes,
        string? concentration,
        string? projection,
        string? longevity,
        string? occasions,
        string? season,
        string? period,
        DateTimeOffset createdAtUtc)
    {
        Name = Required(name, NameMaxLength, nameof(name));
        Slug = CatalogSlug.Normalize(slug);
        ApplyEditorialDetails(
            shortDescription,
            description,
            olfactoryFamily,
            topNotes,
            heartNotes,
            baseNotes,
            concentration,
            projection,
            longevity,
            occasions,
            season,
            period);
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string ShortDescription { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string OlfactoryFamily { get; private set; } = string.Empty;

    public string TopNotes { get; private set; } = string.Empty;

    public string HeartNotes { get; private set; } = string.Empty;

    public string BaseNotes { get; private set; } = string.Empty;

    public string Concentration { get; private set; } = string.Empty;

    public string Projection { get; private set; } = string.Empty;

    public string Longevity { get; private set; } = string.Empty;

    public string Occasions { get; private set; } = string.Empty;

    public string Season { get; private set; } = string.Empty;

    public string Period { get; private set; } = string.Empty;

    public ProductStatus Status { get; private set; } = ProductStatus.Draft;

    public bool IsFeatured { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public string UpdateDetails(
        string name,
        string slug,
        string? shortDescription,
        string? description,
        string? olfactoryFamily,
        string? topNotes,
        string? heartNotes,
        string? baseNotes,
        string? concentration,
        string? projection,
        string? longevity,
        string? occasions,
        string? season,
        string? period,
        DateTimeOffset updatedAtUtc)
    {
        if (Status == ProductStatus.Archived)
        {
            throw new InvalidOperationException(
                "Archived products cannot be edited.");
        }

        if (Status == ProductStatus.Published
            && !HasCompleteEditorialDetails(
                shortDescription,
                description,
                olfactoryFamily,
                topNotes,
                heartNotes,
                baseNotes,
                concentration))
        {
            throw new InvalidOperationException(
                "Published products must retain complete editorial details.");
        }

        var previousSlug = Slug;
        Name = Required(name, NameMaxLength, nameof(name));
        Slug = CatalogSlug.Normalize(slug);
        ApplyEditorialDetails(
            shortDescription,
            description,
            olfactoryFamily,
            topNotes,
            heartNotes,
            baseNotes,
            concentration,
            projection,
            longevity,
            occasions,
            season,
            period);
        Touch(updatedAtUtc);

        return previousSlug;
    }

    public void Publish(
        bool hasImage,
        bool hasSellableVariant,
        DateTimeOffset publishedAtUtc)
    {
        if (Status == ProductStatus.Archived)
        {
            throw new InvalidOperationException(
                "Archived products cannot be published.");
        }

        if (!hasImage)
        {
            throw new InvalidOperationException(
                "A product image is required before publication.");
        }

        if (!hasSellableVariant)
        {
            throw new InvalidOperationException(
                "A sellable product variant is required before publication.");
        }

        if (!HasCompleteEditorialDetails())
        {
            throw new InvalidOperationException(
                "Editorial product details are incomplete.");
        }

        Status = ProductStatus.Published;
        PublishedAtUtc ??= RequireUtc(
            publishedAtUtc,
            nameof(publishedAtUtc));
        Touch(publishedAtUtc);
    }

    public void Archive(DateTimeOffset archivedAtUtc)
    {
        Status = ProductStatus.Archived;
        IsFeatured = false;
        Touch(archivedAtUtc);
    }

    public void SetFeatured(
        bool isFeatured,
        DateTimeOffset updatedAtUtc)
    {
        if (isFeatured && Status != ProductStatus.Published)
        {
            throw new InvalidOperationException(
                "Only published products can be featured.");
        }

        IsFeatured = isFeatured;
        Touch(updatedAtUtc);
    }

    private void ApplyEditorialDetails(
        string? shortDescription,
        string? description,
        string? olfactoryFamily,
        string? topNotes,
        string? heartNotes,
        string? baseNotes,
        string? concentration,
        string? projection,
        string? longevity,
        string? occasions,
        string? season,
        string? period)
    {
        ShortDescription = Optional(
            shortDescription,
            ShortDescriptionMaxLength,
            nameof(shortDescription));
        Description = Optional(
            description,
            DescriptionMaxLength,
            nameof(description));
        OlfactoryFamily = Optional(
            olfactoryFamily,
            ClassificationMaxLength,
            nameof(olfactoryFamily));
        TopNotes = Optional(topNotes, AttributeMaxLength, nameof(topNotes));
        HeartNotes = Optional(
            heartNotes,
            AttributeMaxLength,
            nameof(heartNotes));
        BaseNotes = Optional(baseNotes, AttributeMaxLength, nameof(baseNotes));
        Concentration = Optional(
            concentration,
            ClassificationMaxLength,
            nameof(concentration));
        Projection = Optional(
            projection,
            ClassificationMaxLength,
            nameof(projection));
        Longevity = Optional(
            longevity,
            ClassificationMaxLength,
            nameof(longevity));
        Occasions = Optional(
            occasions,
            AttributeMaxLength,
            nameof(occasions));
        Season = Optional(
            season,
            ClassificationMaxLength,
            nameof(season));
        Period = Optional(
            period,
            ClassificationMaxLength,
            nameof(period));
    }

    private bool HasCompleteEditorialDetails() =>
        HasCompleteEditorialDetails(
            ShortDescription,
            Description,
            OlfactoryFamily,
            TopNotes,
            HeartNotes,
            BaseNotes,
            Concentration);

    private static bool HasCompleteEditorialDetails(
        string? shortDescription,
        string? description,
        string? olfactoryFamily,
        string? topNotes,
        string? heartNotes,
        string? baseNotes,
        string? concentration) =>
        !string.IsNullOrWhiteSpace(shortDescription)
        && !string.IsNullOrWhiteSpace(description)
        && !string.IsNullOrWhiteSpace(olfactoryFamily)
        && !string.IsNullOrWhiteSpace(topNotes)
        && !string.IsNullOrWhiteSpace(heartNotes)
        && !string.IsNullOrWhiteSpace(baseNotes)
        && !string.IsNullOrWhiteSpace(concentration);

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = RequireUtc(timestamp, nameof(timestamp));
        ConcurrencyVersion = Guid.NewGuid();
    }

    private static string Required(
        string value,
        int maxLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }

    private static string Optional(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Required(value, maxLength, parameterName);
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
