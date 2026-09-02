using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Catalog;
using Sallvat.Domain.Catalog;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

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
        await AddImageAsync(application, productId);

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
        await AddImageAsync(application, productId);
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
        long productId)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        context.ProductImages.Add(new ProductImage(
            productId,
            $"products/{productId}/cover.webp",
            "Perfume Teste",
            1200,
            1500,
            0,
            true,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }
}
