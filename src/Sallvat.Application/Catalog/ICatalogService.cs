namespace Sallvat.Application.Catalog;

public interface ICatalogService
{
    Task<CatalogPage> ListPublishedAsync(
        string? family,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CatalogLookupResult> FindPublishedAsync(
        string slug,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminProductSummary>> ListAdminAsync(
        CancellationToken cancellationToken = default);

    Task<AdminProductDetails?> GetAdminAsync(
        long productId,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> CreateProductAsync(
        ProductEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> UpdateProductAsync(
        long productId,
        Guid expectedVersion,
        ProductEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> AddVariantAsync(
        long productId,
        VariantEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> UpdateVariantAsync(
        long productId,
        long variantId,
        Guid expectedVersion,
        VariantEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> PublishAsync(
        long productId,
        Guid expectedVersion,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> ArchiveAsync(
        long productId,
        Guid expectedVersion,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> SetFeaturedAsync(
        long productId,
        Guid expectedVersion,
        bool isFeatured,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<CatalogMutationResult> AdjustStockAsync(
        long productId,
        long variantId,
        Guid expectedVersion,
        int newOnHand,
        string reason,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryMovementView>> ListMovementsAsync(
        long variantId,
        CancellationToken cancellationToken = default);
}
