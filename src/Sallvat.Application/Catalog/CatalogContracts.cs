using Sallvat.Domain.Catalog;

namespace Sallvat.Application.Catalog;

public sealed record CatalogProductSummary(
    long Id,
    string Name,
    string Slug,
    string ShortDescription,
    string OlfactoryFamily,
    decimal StartingPrice,
    CatalogImage? CoverImage);

public sealed record CatalogImage(
    long Id,
    string AltText,
    int Width,
    int Height,
    int Position,
    bool IsCover,
    string OriginalUrl,
    string LargeUrl,
    string ThumbnailUrl);

public sealed record CatalogPage(
    IReadOnlyList<CatalogProductSummary> Items,
    IReadOnlyList<string> Families,
    string? SelectedFamily,
    int Page,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling((double)TotalItems / PageSize));
}

public sealed record CatalogVariant(
    long Id,
    string Sku,
    int VolumeMl,
    decimal Price,
    string Currency,
    int Available);

public sealed record CatalogProductDetails(
    long Id,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    string OlfactoryFamily,
    string TopNotes,
    string HeartNotes,
    string BaseNotes,
    string Concentration,
    string Projection,
    string Longevity,
    string Occasions,
    string Season,
    string Period,
    IReadOnlyList<CatalogImage> Images,
    IReadOnlyList<CatalogVariant> Variants);

public sealed record CatalogLookupResult(
    CatalogProductDetails? Product,
    string? RedirectSlug);

public sealed record ProductEditorInput(
    string Name,
    string Slug,
    string? ShortDescription,
    string? Description,
    string? OlfactoryFamily,
    string? TopNotes,
    string? HeartNotes,
    string? BaseNotes,
    string? Concentration,
    string? Projection,
    string? Longevity,
    string? Occasions,
    string? Season,
    string? Period);

public sealed record VariantEditorInput(
    string Sku,
    int VolumeMl,
    decimal Price,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    bool IsActive);

public sealed record AdminOperationContext(
    Guid ActorUserId,
    string CorrelationId);

public sealed record AdminProductSummary(
    long Id,
    string Name,
    string Slug,
    ProductStatus Status,
    bool IsFeatured,
    int VariantCount,
    int AvailableStock,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminVariant(
    long Id,
    string Sku,
    int VolumeMl,
    decimal Price,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    int OnHand,
    int Reserved,
    bool IsActive,
    Guid ConcurrencyVersion);

public sealed record AdminProductDetails(
    long Id,
    ProductEditorInput Product,
    ProductStatus Status,
    bool IsFeatured,
    Guid ConcurrencyVersion,
    IReadOnlyList<CatalogImage> Images,
    IReadOnlyList<AdminVariant> Variants);

public sealed record ProductImageUpload(
    Stream Content,
    long Length,
    string FileName);

public sealed record ProductImagePresentationInput(
    long ImageId,
    string AltText,
    int Position,
    bool IsCover);

public sealed record InventoryMovementView(
    long Id,
    int Quantity,
    int ResultingOnHand,
    int ResultingReserved,
    string Reason,
    DateTimeOffset CreatedAtUtc);

public enum CatalogMutationStatus
{
    Succeeded,
    NotFound,
    Invalid,
    Duplicate,
    ConcurrencyConflict,
}

public sealed record CatalogMutationResult(
    CatalogMutationStatus Status,
    long? EntityId,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == CatalogMutationStatus.Succeeded;

    public static CatalogMutationResult Success(long? entityId = null) =>
        new(CatalogMutationStatus.Succeeded, entityId, []);

    public static CatalogMutationResult Failure(
        CatalogMutationStatus status,
        params string[] errors) =>
        new(status, null, errors);
}
