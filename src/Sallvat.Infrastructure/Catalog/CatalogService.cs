using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sallvat.Application.Catalog;
using Sallvat.Application.Time;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Inventory;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Storage;

namespace Sallvat.Infrastructure.Catalog;

internal sealed class CatalogService(
    SallvatDbContext dbContext,
    IClock clock,
    IImageStorage imageStorage,
    IImageProcessor imageProcessor,
    IOptions<ImageStorageOptions> imageOptions,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const int MaximumPageSize = 24;
    private static readonly Action<ILogger, string, Exception?>
        LogUnreferencedImageDeleteFailure = LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2101, "UnreferencedImageDeleteFailure"),
            "Could not remove unreferenced product image {StorageKey}");

    public async Task<CatalogPage> ListPublishedAsync(
        string? family,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var selectedFamily = string.IsNullOrWhiteSpace(family)
            ? null
            : family.Trim();

        var publicProducts = PublishedProducts();

        var families = await publicProducts
            .Where(product => product.OlfactoryFamily != string.Empty)
            .Select(product => product.OlfactoryFamily)
            .Distinct()
            .OrderBy(item => item)
            .ToListAsync(cancellationToken);

        if (selectedFamily is not null)
        {
            publicProducts = publicProducts.Where(product =>
                product.OlfactoryFamily == selectedFamily);
        }

        var totalItems = await publicProducts.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / pageSize));
        page = Math.Min(page, totalPages);
        var items = await LoadSummariesAsync(
            publicProducts
            .OrderByDescending(product => product.IsFeatured)
            .ThenBy(product => product.Name)
                .Skip((page - 1) * pageSize),
            pageSize,
            cancellationToken);

        return new CatalogPage(
            items,
            families,
            selectedFamily,
            page,
            pageSize,
            totalItems);
    }

    public Task<IReadOnlyList<CatalogProductSummary>> ListFeaturedAsync(
        int maximumItems,
        CancellationToken cancellationToken = default) =>
        LoadSummariesAsync(
            PublishedProducts()
                .Where(product => product.IsFeatured)
                .OrderByDescending(product => product.PublishedAtUtc)
                .ThenBy(product => product.Name),
            Math.Clamp(maximumItems, 1, 6),
            cancellationToken);

    public async Task<CatalogLookupResult> FindPublishedAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        string normalizedSlug;
        try
        {
            normalizedSlug = CatalogSlug.Normalize(slug);
        }
        catch (ArgumentException)
        {
            return new CatalogLookupResult(null, null);
        }

        var productId = await dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.Slug == normalizedSlug
                && product.Status == ProductStatus.Published)
            .Select(product => (long?)product.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (productId is not null)
        {
            return new CatalogLookupResult(
                await LoadPublishedDetailsAsync(
                    productId.Value,
                    cancellationToken),
                null);
        }

        var redirectSlug = await (
            from history in dbContext.ProductSlugHistory.AsNoTracking()
            join product in dbContext.Products.AsNoTracking()
                on history.ProductId equals product.Id
            where history.Slug == normalizedSlug
                && product.Status == ProductStatus.Published
            select product.Slug)
            .SingleOrDefaultAsync(cancellationToken);

        return new CatalogLookupResult(null, redirectSlug);
    }

    public async Task<IReadOnlyList<AdminProductSummary>> ListAdminAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Products
            .AsNoTracking()
            .OrderByDescending(product => product.UpdatedAtUtc)
            .Select(product => new AdminProductSummary(
                product.Id,
                product.Name,
                product.Slug,
                product.Status,
                product.IsFeatured,
                dbContext.ProductVariants.Count(variant =>
                    variant.ProductId == product.Id),
                dbContext.ProductVariants
                    .Where(variant => variant.ProductId == product.Id)
                    .Sum(variant => (int?)(variant.OnHand - variant.Reserved))
                    ?? 0,
                product.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<AdminProductDetails?> GetAdminAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == productId,
                cancellationToken);
        if (product is null)
        {
            return null;
        }

        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId)
            .OrderBy(variant => variant.VolumeMl)
            .Select(variant => new AdminVariant(
                variant.Id,
                variant.Sku,
                variant.VolumeMl,
                variant.Price,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                variant.OnHand,
                variant.Reserved,
                variant.IsActive,
                variant.ConcurrencyVersion))
            .ToListAsync(cancellationToken);
        var images = await LoadImagesAsync(productId, cancellationToken);

        return new AdminProductDetails(
            product.Id,
            ToEditorInput(product),
            product.Status,
            product.IsFeatured,
            product.ConcurrencyVersion,
            images,
            variants);
    }

    public async Task<CatalogMutationResult> CreateProductAsync(
        ProductEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        Product product;
        try
        {
            product = CreateProduct(input);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }

        if (await SlugExistsAsync(product.Slug, null, cancellationToken))
        {
            return Duplicate("Já existe um produto com esse slug.");
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        dbContext.Products.Add(product);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AddAudit(
                operation,
                "catalog.product.created",
                "Product",
                product.Id,
                new { product.Name, product.Slug });
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            return CatalogMutationResult.Success(product.Id);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Duplicate("Não foi possível criar o produto; confira slug e dados informados.");
        }
    }

    public async Task<CatalogMutationResult> UpdateProductAsync(
        long productId,
        Guid expectedVersion,
        ProductEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        try
        {
            dbContext.Entry(product)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            var previousSlug = product.UpdateDetails(
                input.Name,
                input.Slug,
                input.ShortDescription,
                input.Description,
                input.OlfactoryFamily,
                input.TopNotes,
                input.HeartNotes,
                input.BaseNotes,
                input.Concentration,
                input.Projection,
                input.Longevity,
                input.Occasions,
                input.Season,
                input.Period,
                clock.UtcNow);

            if (await SlugExistsAsync(
                    product.Slug,
                    productId,
                    cancellationToken))
            {
                return Duplicate("Já existe um produto com esse slug.");
            }

            if (!previousSlug.Equals(product.Slug, StringComparison.Ordinal)
                && !await dbContext.ProductSlugHistory.AnyAsync(
                    history => history.Slug == previousSlug,
                    cancellationToken))
            {
                dbContext.ProductSlugHistory.Add(
                    new ProductSlugHistory(
                        productId,
                        previousSlug,
                        clock.UtcNow));
            }

            AddAudit(
                operation,
                "catalog.product.updated",
                "Product",
                product.Id,
                new { product.Name, product.Slug, PreviousSlug = previousSlug });
            await dbContext.SaveChangesAsync(cancellationToken);

            return CatalogMutationResult.Success(product.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (DbUpdateException)
        {
            return Duplicate("Não foi possível salvar; confira se o slug é exclusivo.");
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(exception);
        }
    }

    public async Task<CatalogMutationResult> AddVariantAsync(
        long productId,
        VariantEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var productStatus = await dbContext.Products
            .Where(product => product.Id == productId)
            .Select(product => (ProductStatus?)product.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (productStatus is null)
        {
            return NotFound();
        }

        if (productStatus == ProductStatus.Archived)
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Produtos arquivados não podem receber variantes.");
        }

        ProductVariant variant;
        try
        {
            variant = CreateVariant(productId, input);
            if (!input.IsActive)
            {
                variant.UpdateCommercialData(
                    input.Sku,
                    input.VolumeMl,
                    input.Price,
                    input.WeightKg,
                    input.HeightCm,
                    input.WidthCm,
                    input.LengthCm,
                    false,
                    clock.UtcNow);
            }
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }

        if (await SkuExistsAsync(variant.NormalizedSku, null, cancellationToken))
        {
            return Duplicate("Já existe uma variante com esse SKU.");
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        dbContext.ProductVariants.Add(variant);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AddAudit(
                operation,
                "catalog.variant.created",
                "ProductVariant",
                variant.Id,
                new { variant.ProductId, variant.Sku, variant.VolumeMl });
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            return CatalogMutationResult.Success(variant.Id);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Duplicate("Não foi possível criar a variante; confira o SKU.");
        }
    }

    public async Task<CatalogMutationResult> UpdateVariantAsync(
        long productId,
        long variantId,
        Guid expectedVersion,
        VariantEditorInput input,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var variant = await dbContext.ProductVariants.SingleOrDefaultAsync(
            item => item.Id == variantId && item.ProductId == productId,
            cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        try
        {
            dbContext.Entry(variant)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            variant.UpdateCommercialData(
                input.Sku,
                input.VolumeMl,
                input.Price,
                input.WeightKg,
                input.HeightCm,
                input.WidthCm,
                input.LengthCm,
                input.IsActive,
                clock.UtcNow);
            var productIsPublished = await dbContext.Products.AnyAsync(
                product =>
                    product.Id == productId
                    && product.Status == ProductStatus.Published,
                cancellationToken);
            if (productIsPublished
                && !variant.IsSellable
                && !await dbContext.ProductVariants.AnyAsync(
                    other =>
                        other.ProductId == productId
                        && other.Id != variantId
                        && other.IsActive
                        && other.Price > 0
                        && other.WeightKg > 0
                        && other.HeightCm > 0
                        && other.WidthCm > 0
                        && other.LengthCm > 0,
                    cancellationToken))
            {
                return CatalogMutationResult.Failure(
                    CatalogMutationStatus.Invalid,
                    "Um produto publicado deve manter ao menos uma variante comercializável.");
            }

            if (await SkuExistsAsync(
                    variant.NormalizedSku,
                    variantId,
                    cancellationToken))
            {
                return Duplicate("Já existe uma variante com esse SKU.");
            }

            AddAudit(
                operation,
                "catalog.variant.updated",
                "ProductVariant",
                variant.Id,
                new { variant.Sku, variant.VolumeMl, variant.Price, variant.IsActive });
            await dbContext.SaveChangesAsync(cancellationToken);

            return CatalogMutationResult.Success(variant.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (DbUpdateException)
        {
            return Duplicate("Não foi possível salvar; confira se o SKU é exclusivo.");
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
    }

    public Task<CatalogMutationResult> PublishAsync(
        long productId,
        Guid expectedVersion,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default) =>
        ChangeProductAsync(
            productId,
            expectedVersion,
            operation,
            "catalog.product.published",
            async product => product.Publish(
                await dbContext.ProductImages.AnyAsync(
                    image => image.ProductId == productId,
                    cancellationToken),
                await dbContext.ProductVariants.AnyAsync(
                    variant =>
                        variant.ProductId == productId
                        && variant.IsActive
                        && variant.Price > 0
                        && variant.WeightKg > 0
                        && variant.HeightCm > 0
                        && variant.WidthCm > 0
                        && variant.LengthCm > 0,
                    cancellationToken),
                clock.UtcNow),
            cancellationToken);

    public Task<CatalogMutationResult> ArchiveAsync(
        long productId,
        Guid expectedVersion,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default) =>
        ChangeProductAsync(
            productId,
            expectedVersion,
            operation,
            "catalog.product.archived",
            product =>
            {
                product.Archive(clock.UtcNow);
                return Task.CompletedTask;
            },
            cancellationToken);

    public Task<CatalogMutationResult> SetFeaturedAsync(
        long productId,
        Guid expectedVersion,
        bool isFeatured,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default) =>
        ChangeProductAsync(
            productId,
            expectedVersion,
            operation,
            isFeatured
                ? "catalog.product.featured"
                : "catalog.product.unfeatured",
            product =>
            {
                product.SetFeatured(isFeatured, clock.UtcNow);
                return Task.CompletedTask;
            },
            cancellationToken);

    public async Task<CatalogMutationResult> AdjustStockAsync(
        long productId,
        long variantId,
        Guid expectedVersion,
        int newOnHand,
        string reason,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var variant = await dbContext.ProductVariants.SingleOrDefaultAsync(
            item => item.Id == variantId && item.ProductId == productId,
            cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        try
        {
            dbContext.Entry(variant)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            var difference = variant.AdjustOnHand(newOnHand, clock.UtcNow);
            dbContext.InventoryMovements.Add(new InventoryMovement(
                variant.Id,
                InventoryMovementType.ManualAdjustment,
                difference,
                variant.OnHand,
                variant.Reserved,
                operation.ActorUserId,
                reason,
                clock.UtcNow));
            AddAudit(
                operation,
                "inventory.stock.adjusted",
                "ProductVariant",
                variant.Id,
                new { Difference = difference, variant.OnHand, variant.Reserved, Reason = reason.Trim() });
            await dbContext.SaveChangesAsync(cancellationToken);

            return CatalogMutationResult.Success(variant.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(exception);
        }
    }

    public async Task<CatalogMutationResult> AddImageAsync(
        long productId,
        Guid expectedVersion,
        ProductImageUpload upload,
        string altText,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(altText)
            || altText.Trim().Length > ProductImage.AltTextMaxLength)
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Informe um texto alternativo de até 200 caracteres.");
        }

        var imageCount = await dbContext.ProductImages.CountAsync(
            image => image.ProductId == productId,
            cancellationToken);
        if (imageCount >= imageOptions.Value.MaximumImagesPerProduct)
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                $"Cada produto pode ter até {imageOptions.Value.MaximumImagesPerProduct} imagens.");
        }

        ProcessedImage processed;
        try
        {
            processed = await imageProcessor.ProcessAsync(
                upload,
                cancellationToken);
            dbContext.Entry(product)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            product.RecordImageChange(clock.UtcNow);
        }
        catch (ImageProcessingException exception)
        {
            return Invalid(exception);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(exception);
        }

        var baseKey = $"products/{productId}/{Guid.NewGuid():N}";
        var storedKeys = new List<string>(3);
        try
        {
            await WriteImageAsync(
                OriginalKey(baseKey),
                processed.Original,
                storedKeys,
                cancellationToken);
            await WriteImageAsync(
                LargeKey(baseKey),
                processed.Large,
                storedKeys,
                cancellationToken);
            await WriteImageAsync(
                ThumbnailKey(baseKey),
                processed.Thumbnail,
                storedKeys,
                cancellationToken);

            var lastPosition = await dbContext.ProductImages
                .Where(image => image.ProductId == productId)
                .Select(image => (int?)image.Position)
                .MaxAsync(cancellationToken);
            var image = new ProductImage(
                productId,
                baseKey,
                altText,
                processed.Width,
                processed.Height,
                lastPosition.GetValueOrDefault(-1) + 1,
                imageCount == 0,
                clock.UtcNow);
            dbContext.ProductImages.Add(image);

            await using var transaction = await BeginTransactionAsync(
                cancellationToken);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                AddAudit(
                    operation,
                    "catalog.product-image.created",
                    "ProductImage",
                    image.Id,
                    new { image.ProductId, image.Position, image.IsCover });
                await dbContext.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
            }
            catch
            {
                await RollbackAsync(transaction, cancellationToken);
                throw;
            }

            return CatalogMutationResult.Success(image.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await DeleteStoredKeysAsync(storedKeys, CancellationToken.None);
            return Conflict();
        }
        catch (DbUpdateException)
        {
            await DeleteStoredKeysAsync(storedKeys, CancellationToken.None);
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Não foi possível registrar a imagem.");
        }
        catch
        {
            await DeleteStoredKeysAsync(storedKeys, CancellationToken.None);
            throw;
        }
    }

    public async Task<CatalogMutationResult> UpdateImagesAsync(
        long productId,
        Guid expectedVersion,
        IReadOnlyList<ProductImagePresentationInput> images,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);
        var product = await dbContext.Products.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var storedImages = await dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .OrderBy(image => image.Position)
            .ToListAsync(cancellationToken);
        if (storedImages.Count == 0
            || images.Count != storedImages.Count
            || images.Select(image => image.ImageId).Distinct().Count()
                != storedImages.Count
            || images.Any(input => storedImages.All(
                image => image.Id != input.ImageId)))
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "A lista de imagens está desatualizada. Recarregue a página.");
        }

        if (images.Count(image => image.IsCover) != 1
            || images.Select(image => image.Position).Distinct().Count()
                != images.Count
            || !images.Select(image => image.Position)
                .Order()
                .SequenceEqual(Enumerable.Range(0, images.Count)))
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Defina uma única capa e posições contínuas a partir de zero.");
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            dbContext.Entry(product)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            product.RecordImageChange(clock.UtcNow);

            foreach (var image in storedImages)
            {
                var input = images.Single(item => item.ImageId == image.Id);
                image.UpdatePresentation(input.AltText, input.Position, false);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var image in storedImages)
            {
                var input = images.Single(item => item.ImageId == image.Id);
                image.UpdatePresentation(
                    input.AltText,
                    input.Position,
                    input.IsCover);
            }

            AddAudit(
                operation,
                "catalog.product-images.updated",
                "Product",
                productId,
                images.Select(image => new
                {
                    image.ImageId,
                    image.Position,
                    image.IsCover,
                }));
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return CatalogMutationResult.Success(productId);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Conflict();
        }
        catch (ArgumentException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Invalid(exception);
        }
        catch (InvalidOperationException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Invalid(exception);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Não foi possível reorganizar as imagens.");
        }
    }

    public async Task<CatalogMutationResult> RemoveImageAsync(
        long productId,
        long imageId,
        Guid expectedVersion,
        AdminOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var images = await dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .OrderBy(image => image.Position)
            .ToListAsync(cancellationToken);
        var removed = images.SingleOrDefault(image => image.Id == imageId);
        if (removed is null)
        {
            return NotFound();
        }

        if (product.Status == ProductStatus.Published && images.Count == 1)
        {
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Um produto publicado deve manter ao menos uma imagem.");
        }

        var wasCover = removed.IsCover;
        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            dbContext.Entry(product)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            product.RecordImageChange(clock.UtcNow);
            if (wasCover)
            {
                removed.UpdatePresentation(
                    removed.AltText,
                    removed.Position,
                    false);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ProductImages.Remove(removed);
            var remaining = images.Where(image => image.Id != imageId).ToList();
            for (var position = 0; position < remaining.Count; position++)
            {
                var image = remaining[position];
                image.UpdatePresentation(
                    image.AltText,
                    position,
                    wasCover && position == 0 || image.IsCover);
            }

            AddAudit(
                operation,
                "catalog.product-image.removed",
                "ProductImage",
                imageId,
                new { ProductId = productId, WasCover = wasCover });
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Conflict();
        }
        catch (InvalidOperationException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return Invalid(exception);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CatalogMutationResult.Failure(
                CatalogMutationStatus.Invalid,
                "Não foi possível remover a imagem.");
        }

        await DeleteImageFilesAsync(
            removed.StorageKey,
            CancellationToken.None);
        return CatalogMutationResult.Success(productId);
    }

    public async Task<IReadOnlyList<InventoryMovementView>>
        ListMovementsAsync(
            long variantId,
            CancellationToken cancellationToken = default) =>
        await dbContext.InventoryMovements
            .AsNoTracking()
            .Where(movement => movement.ProductVariantId == variantId)
            .OrderByDescending(movement => movement.CreatedAtUtc)
            .Take(100)
            .Select(movement => new InventoryMovementView(
                movement.Id,
                movement.Quantity,
                movement.ResultingOnHand,
                movement.ResultingReserved,
                movement.Reason,
                movement.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    private async Task<CatalogProductDetails?> LoadPublishedDetailsAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == productId
                    && item.Status == ProductStatus.Published,
                cancellationToken);
        if (product is null)
        {
            return null;
        }

        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.ProductId == productId
                && variant.IsActive
                && variant.Price > 0
                && variant.WeightKg > 0
                && variant.HeightCm > 0
                && variant.WidthCm > 0
                && variant.LengthCm > 0)
            .OrderBy(variant => variant.VolumeMl)
            .Select(variant => new CatalogVariant(
                variant.Id,
                variant.Sku,
                variant.VolumeMl,
                variant.Price,
                variant.Currency,
                variant.OnHand - variant.Reserved))
            .ToListAsync(cancellationToken);
        var images = await LoadImagesAsync(productId, cancellationToken);

        return new CatalogProductDetails(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.Description,
            product.OlfactoryFamily,
            product.TopNotes,
            product.HeartNotes,
            product.BaseNotes,
            product.Concentration,
            product.Projection,
            product.Longevity,
            product.Occasions,
            product.Season,
            product.Period,
            images,
            variants);
    }

    private async Task<CatalogMutationResult> ChangeProductAsync(
        long productId,
        Guid expectedVersion,
        AdminOperationContext operation,
        string action,
        Func<Product, Task> change,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        try
        {
            dbContext.Entry(product)
                .Property(item => item.ConcurrencyVersion)
                .OriginalValue = expectedVersion;
            await change(product);
            AddAudit(
                operation,
                action,
                "Product",
                product.Id,
                new { product.Status, product.IsFeatured });
            await dbContext.SaveChangesAsync(cancellationToken);

            return CatalogMutationResult.Success(product.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(exception);
        }
    }

    private Product CreateProduct(ProductEditorInput input) =>
        new(
            input.Name,
            input.Slug,
            input.ShortDescription,
            input.Description,
            input.OlfactoryFamily,
            input.TopNotes,
            input.HeartNotes,
            input.BaseNotes,
            input.Concentration,
            input.Projection,
            input.Longevity,
            input.Occasions,
            input.Season,
            input.Period,
            clock.UtcNow);

    private ProductVariant CreateVariant(
        long productId,
        VariantEditorInput input) =>
        new(
            productId,
            input.Sku,
            input.VolumeMl,
            input.Price,
            input.WeightKg,
            input.HeightCm,
            input.WidthCm,
            input.LengthCm,
            clock.UtcNow);

    private static ProductEditorInput ToEditorInput(Product product) =>
        new(
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.Description,
            product.OlfactoryFamily,
            product.TopNotes,
            product.HeartNotes,
            product.BaseNotes,
            product.Concentration,
            product.Projection,
            product.Longevity,
            product.Occasions,
            product.Season,
            product.Period);

    private IQueryable<Product> PublishedProducts() =>
        dbContext.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Published)
            .Where(product => dbContext.ProductVariants.Any(variant =>
                variant.ProductId == product.Id
                && variant.IsActive
                && variant.Price > 0
                && variant.WeightKg > 0
                && variant.HeightCm > 0
                && variant.WidthCm > 0
                && variant.LengthCm > 0));

    private async Task<IReadOnlyList<CatalogProductSummary>>
        LoadSummariesAsync(
            IQueryable<Product> products,
            int maximumItems,
            CancellationToken cancellationToken)
    {
        var rows = await products
            .Take(maximumItems)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Slug,
                product.ShortDescription,
                product.OlfactoryFamily,
                StartingPrice = dbContext.ProductVariants
                    .Where(variant =>
                        variant.ProductId == product.Id
                        && variant.IsActive
                        && variant.Price > 0
                        && variant.WeightKg > 0
                        && variant.HeightCm > 0
                        && variant.WidthCm > 0
                        && variant.LengthCm > 0)
                    .Min(variant => variant.Price),
                Cover = dbContext.ProductImages
                    .Where(image =>
                        image.ProductId == product.Id && image.IsCover)
                    .Select(image => new
                    {
                        image.Id,
                        image.StorageKey,
                        image.AltText,
                        image.Width,
                        image.Height,
                        image.Position,
                        image.IsCover,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CatalogProductSummary(
                row.Id,
                row.Name,
                row.Slug,
                row.ShortDescription,
                row.OlfactoryFamily,
                row.StartingPrice,
                row.Cover is null
                    ? null
                    : ToCatalogImage(
                        row.Cover.Id,
                        row.Cover.StorageKey,
                        row.Cover.AltText,
                        row.Cover.Width,
                        row.Cover.Height,
                        row.Cover.Position,
                        row.Cover.IsCover)))
            .ToList();
    }

    private async Task<IReadOnlyList<CatalogImage>> LoadImagesAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var images = await dbContext.ProductImages
            .AsNoTracking()
            .Where(image => image.ProductId == productId)
            .OrderByDescending(image => image.IsCover)
            .ThenBy(image => image.Position)
            .ToListAsync(cancellationToken);

        return images
            .Select(image => ToCatalogImage(
                image.Id,
                image.StorageKey,
                image.AltText,
                image.Width,
                image.Height,
                image.Position,
                image.IsCover))
            .ToList();
    }

    private CatalogImage ToCatalogImage(
        long id,
        string baseKey,
        string altText,
        int width,
        int height,
        int position,
        bool isCover) =>
        new(
            id,
            altText,
            width,
            height,
            position,
            isCover,
            imageStorage.GetPublicUrl(OriginalKey(baseKey)),
            imageStorage.GetPublicUrl(LargeKey(baseKey)),
            imageStorage.GetPublicUrl(ThumbnailKey(baseKey)));

    private async Task WriteImageAsync(
        string key,
        byte[] content,
        List<string> storedKeys,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content, writable: false);
        await imageStorage.WriteAsync(key, stream, cancellationToken);
        storedKeys.Add(key);
    }

    private async Task DeleteImageFilesAsync(
        string baseKey,
        CancellationToken cancellationToken) =>
        await DeleteStoredKeysAsync(
            [
                OriginalKey(baseKey),
                LargeKey(baseKey),
                ThumbnailKey(baseKey),
            ],
            cancellationToken);

    private async Task DeleteStoredKeysAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        foreach (var key in keys)
        {
            try
            {
                await imageStorage.DeleteAsync(key, cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                LogUnreferencedImageDeleteFailure(logger, key, exception);
            }
        }
    }

    private static string OriginalKey(string baseKey) =>
        $"{baseKey}/original.webp";

    private static string LargeKey(string baseKey) =>
        $"{baseKey}/large.webp";

    private static string ThumbnailKey(string baseKey) =>
        $"{baseKey}/thumb.webp";

    private Task<bool> SlugExistsAsync(
        string slug,
        long? exceptProductId,
        CancellationToken cancellationToken) =>
        dbContext.Products.AnyAsync(
            product =>
                product.Slug == slug
                && (!exceptProductId.HasValue
                    || product.Id != exceptProductId.Value),
            cancellationToken);

    private Task<bool> SkuExistsAsync(
        string normalizedSku,
        long? exceptVariantId,
        CancellationToken cancellationToken) =>
        dbContext.ProductVariants.AnyAsync(
            variant =>
                variant.NormalizedSku == normalizedSku
                && (!exceptVariantId.HasValue
                    || variant.Id != exceptVariantId.Value),
            cancellationToken);

    private void AddAudit(
        AdminOperationContext operation,
        string action,
        string entityType,
        long entityId,
        object changes) =>
        dbContext.AuditLogs.Add(new AuditLog(
            operation.ActorUserId,
            action,
            entityType,
            entityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(changes),
            operation.CorrelationId,
            clock.UtcNow));

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static CatalogMutationResult NotFound() =>
        CatalogMutationResult.Failure(
            CatalogMutationStatus.NotFound,
            "O item solicitado não foi encontrado.");

    private static CatalogMutationResult Duplicate(string message) =>
        CatalogMutationResult.Failure(
            CatalogMutationStatus.Duplicate,
            message);

    private static CatalogMutationResult Conflict() =>
        CatalogMutationResult.Failure(
            CatalogMutationStatus.ConcurrencyConflict,
            "O item foi alterado por outra pessoa. Recarregue a página e tente novamente.");

    private static CatalogMutationResult Invalid(Exception exception) =>
        CatalogMutationResult.Failure(
            CatalogMutationStatus.Invalid,
            exception.Message);
}
