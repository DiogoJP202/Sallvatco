using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Catalog;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;
using SkiaSharp;

namespace Sallvat.IntegrationTests.Catalog;

internal static class PublishedCatalogFixture
{
    public static async Task<PublishedProductData> CreateAsync(
        AccountWebApplicationFactory application,
        string slug,
        bool featured = true)
    {
        var actorId = await CreateActorAsync(application);
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var created = await service.CreateProductAsync(
            ProductInput(slug),
            Operation(actorId));
        var productId = Assert.IsType<long>(created.EntityId);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var firstVariant = await service.AddVariantAsync(
            productId,
            new VariantEditorInput(
                $"SAL-{suffix}-50",
                50,
                299.90m,
                0.4m,
                12m,
                8m,
                8m,
                true),
            Operation(actorId));
        var secondVariant = await service.AddVariantAsync(
            productId,
            new VariantEditorInput(
                $"SAL-{suffix}-100",
                100,
                449.90m,
                0.7m,
                15m,
                10m,
                10m,
                true),
            Operation(actorId));
        var firstVariantId = Assert.IsType<long>(firstVariant.EntityId);
        var secondVariantId = Assert.IsType<long>(secondVariant.EntityId);
        var product = await service.GetAdminAsync(productId);
        var firstAdminVariant = product!.Variants.Single(
            variant => variant.Id == firstVariantId);
        var stock = await service.AdjustStockAsync(
            productId,
            firstVariantId,
            firstAdminVariant.ConcurrencyVersion,
            4,
            "Estoque de teste",
            Operation(actorId));
        Assert.True(stock.Succeeded);

        var bytes = CreatePng();
        using var content = new MemoryStream(bytes);
        product = await service.GetAdminAsync(productId);
        var image = await service.AddImageAsync(
            productId,
            product!.ConcurrencyVersion,
            new ProductImageUpload(content, bytes.Length, "ambar.png"),
            "Frasco de Âmbar Noturno sobre fundo claro",
            Operation(actorId));
        Assert.True(image.Succeeded);

        product = await service.GetAdminAsync(productId);
        var published = await service.PublishAsync(
            productId,
            product!.ConcurrencyVersion,
            Operation(actorId));
        Assert.True(published.Succeeded);
        if (featured)
        {
            product = await service.GetAdminAsync(productId);
            var highlighted = await service.SetFeaturedAsync(
                productId,
                product!.ConcurrencyVersion,
                true,
                Operation(actorId));
            Assert.True(highlighted.Succeeded);
        }

        return new PublishedProductData(
            productId,
            actorId,
            slug,
            firstVariantId,
            secondVariantId);
    }

    public static ProductEditorInput ProductInput(string slug) =>
        new(
            "Âmbar Noturno",
            slug,
            "Uma composição amadeirada de presença elegante.",
            "Bergamota luminosa encontra íris e sândalo em uma composição de evolução serena.",
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

    private static async Task<Guid> CreateActorAsync(
        AccountWebApplicationFactory application)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var email = $"catalog-{Guid.NewGuid():N}@example.invalid";
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

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(800, 1000);
        bitmap.Erase(new SKColor(119, 74, 54));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static AdminOperationContext Operation(Guid actorId) =>
        new(actorId, "published-catalog-fixture");
}

internal sealed record PublishedProductData(
    long ProductId,
    Guid ActorId,
    string Slug,
    long AvailableVariantId,
    long OutOfStockVariantId);
