using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Catalog;
using Sallvat.Domain.Catalog;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;
using SkiaSharp;

namespace Sallvat.IntegrationTests.Catalog;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task DraftIsNotPublicAndDuplicateSlugIsRejected()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await service.CreateProductAsync(
            ProductInput("perfume-teste"),
            Operation(actorId));
        var duplicate = await service.CreateProductAsync(
            ProductInput("perfume-teste"),
            Operation(actorId));
        var firstVariant = await service.AddVariantAsync(
            Assert.IsType<long>(created.EntityId),
            VariantInput("SKU-DUPLICADO"),
            Operation(actorId));
        var duplicateVariant = await service.AddVariantAsync(
            Assert.IsType<long>(created.EntityId),
            VariantInput("sku-duplicado"),
            Operation(actorId));
        var catalog = await service.ListPublishedAsync(null, 1, 12);

        Assert.True(created.Succeeded);
        Assert.Equal(CatalogMutationStatus.Duplicate, duplicate.Status);
        Assert.True(firstVariant.Succeeded);
        Assert.Equal(
            CatalogMutationStatus.Duplicate,
            duplicateVariant.Status);
        Assert.Empty(catalog.Items);
    }

    [Fact]
    public async Task PublicationRequiresImageAndVariantThenExposesProduct()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput("perfume-publicado"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var product = await service.GetAdminAsync(productId);

        var blocked = await service.PublishAsync(
            productId,
            product!.ConcurrencyVersion,
            Operation(actorId));
        Assert.Equal(CatalogMutationStatus.Invalid, blocked.Status);

        var variant = await service.AddVariantAsync(
            productId,
            VariantInput("SAL-PUBLICADO"),
            Operation(actorId));
        Assert.True(variant.Succeeded);
        await AddImageAsync(application, productId, actorId);

        product = await service.GetAdminAsync(productId);
        var published = await service.PublishAsync(
            productId,
            product!.ConcurrencyVersion,
            Operation(actorId));
        var catalog = await service.ListPublishedAsync(null, 1, 12);

        Assert.True(published.Succeeded);
        var item = Assert.Single(catalog.Items);
        Assert.Equal("perfume-publicado", item.Slug);
    }

    [Fact]
    public async Task StaleProductVersionIsRejected()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        long productId;
        Guid staleVersion;

        using (var firstScope = application.Services.CreateScope())
        {
            var service = firstScope.ServiceProvider
                .GetRequiredService<ICatalogService>();
            var created = await service.CreateProductAsync(
                ProductInput("concorrencia"),
                Operation(actorId));
            productId = Assert.IsType<long>(created.EntityId);
            staleVersion = (await service.GetAdminAsync(productId))!
                .ConcurrencyVersion;
        }

        using (var secondScope = application.Services.CreateScope())
        {
            var service = secondScope.ServiceProvider
                .GetRequiredService<ICatalogService>();
            var updated = await service.UpdateProductAsync(
                productId,
                staleVersion,
                ProductInput("concorrencia-atualizada"),
                Operation(actorId));
            Assert.True(updated.Succeeded);
        }

        using var thirdScope = application.Services.CreateScope();
        var staleService = thirdScope.ServiceProvider
            .GetRequiredService<ICatalogService>();
        var staleUpdate = await staleService.UpdateProductAsync(
            productId,
            staleVersion,
            ProductInput("concorrencia-obsoleta"),
            Operation(actorId));

        Assert.Equal(
            CatalogMutationStatus.ConcurrencyConflict,
            staleUpdate.Status);
    }

    [Fact]
    public async Task StockAdjustmentCreatesMovementAndAudit()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var created = await service.CreateProductAsync(
            ProductInput("estoque"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var added = await service.AddVariantAsync(
            productId,
            VariantInput("SAL-ESTOQUE"),
            Operation(actorId));
        var variantId = Assert.IsType<long>(added.EntityId);
        var product = await service.GetAdminAsync(productId);
        var variant = Assert.Single(product!.Variants);

        var adjusted = await service.AdjustStockAsync(
            productId,
            variantId,
            variant.ConcurrencyVersion,
            8,
            "Entrada conferida",
            Operation(actorId));

        Assert.True(adjusted.Succeeded);
        var movement = Assert.Single(await service.ListMovementsAsync(variantId));
        Assert.Equal(8, movement.Quantity);
        Assert.Equal(8, movement.ResultingOnHand);
        Assert.Contains(
            await context.AuditLogs.ToListAsync(),
            audit => audit.Action == "inventory.stock.adjusted");

        var trackedVariant = await context.ProductVariants.SingleAsync(
            item => item.Id == variantId);
        context.Entry(trackedVariant)
            .Property(item => item.Reserved)
            .CurrentValue = 3;
        await context.SaveChangesAsync();
        var current = Assert.Single(
            (await service.GetAdminAsync(productId))!.Variants);
        var belowReserved = await service.AdjustStockAsync(
            productId,
            variantId,
            current.ConcurrencyVersion,
            2,
            "Ajuste inválido",
            Operation(actorId));
        Assert.Equal(CatalogMutationStatus.Invalid, belowReserved.Status);
    }

    [Fact]
    public async Task PreviousSlugRedirectsToPublishedCanonicalSlug()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput("slug-antigo"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        await service.AddVariantAsync(
            productId,
            VariantInput("SAL-SLUG"),
            Operation(actorId));
        await AddImageAsync(application, productId, actorId);
        var product = await service.GetAdminAsync(productId);
        await service.PublishAsync(
            productId,
            product!.ConcurrencyVersion,
            Operation(actorId));
        product = await service.GetAdminAsync(productId);
        await service.UpdateProductAsync(
            productId,
            product!.ConcurrencyVersion,
            ProductInput("slug-novo"),
            Operation(actorId));

        var lookup = await service.FindPublishedAsync("slug-antigo");

        Assert.Null(lookup.Product);
        Assert.Equal("slug-novo", lookup.RedirectSlug);
    }

    [Fact]
    public async Task ValidImageIsConvertedStoredAndServedWithImmutableCache()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput("imagem-valida"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var product = await service.GetAdminAsync(productId);
        var bytes = CreatePng(800, 1000);
        using var content = new MemoryStream(bytes);

        var added = await service.AddImageAsync(
            productId,
            product!.ConcurrencyVersion,
            new ProductImageUpload(content, bytes.Length, "frasco.png"),
            "Frasco âmbar sobre fundo claro",
            Operation(actorId));

        Assert.True(added.Succeeded);
        product = await service.GetAdminAsync(productId);
        var image = Assert.Single(product!.Images);
        Assert.True(image.IsCover);
        Assert.Equal(800, image.Width);
        Assert.Equal(1000, image.Height);
        Assert.Equal(3, StoredFileCount(application));

        using var client = application.CreateClient();
        using var response = await client.GetAsync(image.ThumbnailUrl);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains(
            "immutable",
            response.Headers.CacheControl?.Extensions.Select(
                extension => extension.Name) ?? []);
        Assert.Equal(
            "nosniff",
            response.Headers.GetValues("X-Content-Type-Options").Single());
        var thumbnailBytes = await response.Content.ReadAsByteArrayAsync();
        using var thumbnailData = SKData.CreateCopy(thumbnailBytes);
        using var thumbnailCodec = SKCodec.Create(thumbnailData);
        Assert.NotNull(thumbnailCodec);
        Assert.Equal(SKEncodedImageFormat.Webp, thumbnailCodec.EncodedFormat);
        Assert.Equal(480, thumbnailCodec.Info.Width);
        Assert.Equal(600, thumbnailCodec.Info.Height);
    }

    [Fact]
    public async Task DisguisedImageIsRejectedWithoutStoredFiles()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput("imagem-disfarcada"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var product = await service.GetAdminAsync(productId);
        var bytes = CreatePng(20, 20);
        using var content = new MemoryStream(bytes);

        var result = await service.AddImageAsync(
            productId,
            product!.ConcurrencyVersion,
            new ProductImageUpload(content, bytes.Length, "ataque.jpg"),
            "Imagem inválida",
            Operation(actorId));

        Assert.Equal(CatalogMutationStatus.Invalid, result.Status);
        Assert.Equal(0, StoredFileCount(application));
    }

    [Fact]
    public async Task ExcessiveDimensionsLengthAndTraversalAreRejected()
    {
        await using var application = new AccountWebApplicationFactory(
            maximumPixelCount: 1_000_000);
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var storage = scope.ServiceProvider.GetRequiredService<IImageStorage>();
        var created = await service.CreateProductAsync(
            ProductInput("limites-imagem"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var product = await service.GetAdminAsync(productId);
        var bytes = CreatePng(1100, 1000);
        using var content = new MemoryStream(bytes);

        var dimensionResult = await service.AddImageAsync(
            productId,
            product!.ConcurrencyVersion,
            new ProductImageUpload(content, bytes.Length, "grande.png"),
            "Imagem grande",
            Operation(actorId));
        using var shortContent = new MemoryStream(CreatePng(10, 10));
        var lengthResult = await service.AddImageAsync(
            productId,
            product.ConcurrencyVersion,
            new ProductImageUpload(
                shortContent,
                10 * 1024 * 1024L + 1,
                "pesada.png"),
            "Imagem pesada",
            Operation(actorId));
        using var traversalContent = new MemoryStream([1, 2, 3]);

        Assert.Equal(CatalogMutationStatus.Invalid, dimensionResult.Status);
        Assert.Equal(CatalogMutationStatus.Invalid, lengthResult.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.WriteAsync(
            "../fora.webp",
            traversalContent));
        Assert.Equal(0, StoredFileCount(application));
    }

    [Fact]
    public async Task ImageOrderCoverAndRemovalRemainConsistent()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput("galeria"),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        await AddImageAsync(application, productId, actorId, "Primeira");
        await AddImageAsync(application, productId, actorId, "Segunda");
        var product = await service.GetAdminAsync(productId);
        var first = product!.Images.Single(image => image.AltText == "Primeira");
        var second = product.Images.Single(image => image.AltText == "Segunda");

        var updated = await service.UpdateImagesAsync(
            productId,
            product.ConcurrencyVersion,
            [
                new(first.Id, "Primeira revisada", 1, false),
                new(second.Id, "Segunda revisada", 0, true),
            ],
            Operation(actorId));
        Assert.True(updated.Succeeded);

        product = await service.GetAdminAsync(productId);
        Assert.Equal(second.Id, product!.Images[0].Id);
        Assert.True(product.Images[0].IsCover);
        var removed = await service.RemoveImageAsync(
            productId,
            second.Id,
            product.ConcurrencyVersion,
            Operation(actorId));

        Assert.True(removed.Succeeded);
        product = await service.GetAdminAsync(productId);
        var remaining = Assert.Single(product!.Images);
        Assert.Equal(first.Id, remaining.Id);
        Assert.True(remaining.IsCover);
        Assert.Equal(0, remaining.Position);
        Assert.Equal(3, StoredFileCount(application));
    }

    [Fact]
    public async Task StaleImageUploadRemovesItsOrphanedFiles()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var actorId = await CreateActorAsync(application);
        long productId;
        Guid staleVersion;
        using (var scope = application.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var created = await service.CreateProductAsync(
                ProductInput("imagem-concorrente"),
                Operation(actorId));
            productId = Assert.IsType<long>(created.EntityId);
            staleVersion = (await service.GetAdminAsync(productId))!
                .ConcurrencyVersion;
        }

        var first = await UploadImageAsync(
            application,
            productId,
            staleVersion,
            actorId,
            "Primeira");
        var stale = await UploadImageAsync(
            application,
            productId,
            staleVersion,
            actorId,
            "Obsoleta");

        Assert.True(first.Succeeded);
        Assert.Equal(CatalogMutationStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(3, StoredFileCount(application));
    }

    private static ProductEditorInput ProductInput(string slug) =>
        new(
            "Perfume Teste",
            slug,
            "Descrição curta.",
            "Descrição completa.",
            "Amadeirado",
            "Bergamota",
            "Íris",
            "Sândalo",
            "Eau de parfum",
            "Moderada",
            "8 horas",
            "Noite",
            "Outono",
            "Noturno");

    private static VariantEditorInput VariantInput(string sku) =>
        new(sku, 50, 299.90m, 0.4m, 12m, 8m, 8m, true);

    private static AdminOperationContext Operation(Guid actorId) =>
        new(actorId, "catalog-integration-test");

    private static async Task<Guid> CreateActorAsync(
        AccountWebApplicationFactory application)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var email = $"admin-{Guid.NewGuid():N}@example.invalid";
        var actor = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
        };
        context.Users.Add(actor);
        await context.SaveChangesAsync();

        return actor.Id;
    }

    private static async Task AddImageAsync(
        AccountWebApplicationFactory application,
        long productId,
        Guid actorId,
        string altText = "Perfume Teste")
    {
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var version = (await service.GetAdminAsync(productId))!
            .ConcurrencyVersion;
        var result = await UploadImageAsync(
            application,
            productId,
            version,
            actorId,
            altText);
        Assert.True(result.Succeeded);
    }

    private static async Task<CatalogMutationResult> UploadImageAsync(
        AccountWebApplicationFactory application,
        long productId,
        Guid version,
        Guid actorId,
        string altText)
    {
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var bytes = CreatePng(40, 50);
        using var content = new MemoryStream(bytes);
        return await service.AddImageAsync(
            productId,
            version,
            new ProductImageUpload(content, bytes.Length, "produto.png"),
            altText,
            Operation(actorId));
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(135, 78, 52));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static int StoredFileCount(
        AccountWebApplicationFactory application) =>
        Directory.Exists(application.ImageStoragePath)
            ? Directory.GetFiles(
                application.ImageStoragePath,
                "*.webp",
                SearchOption.AllDirectories).Length
            : 0;
}
