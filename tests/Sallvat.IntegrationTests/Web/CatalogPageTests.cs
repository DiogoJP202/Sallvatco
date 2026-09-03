using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Catalog;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.Web.Models.Catalog;

namespace Sallvat.IntegrationTests.Web;

public sealed class CatalogPageTests
{
    [Fact]
    public void ProductInputModelDoesNotBindLifecycleOrFeaturedState()
    {
        var propertyNames = typeof(ProductFormViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Status", propertyNames);
        Assert.DoesNotContain("IsFeatured", propertyNames);
    }

    [Fact]
    public async Task EmptyCatalogHasSafeCuratedState()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = application.CreateClient();

        using var response = await client.GetAsync(
            "/perfumes?familia=Floral&pagina=2");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Novas fragrâncias chegarão", content, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "<link rel=\"canonical\" href=\"https://tests.sallvat.invalid/perfumes\"",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousVisitorCannotAccessProductAdministration()
    {
        await using var application = new AccountWebApplicationFactory();
        using var client = application.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

        using var response = await client.GetAsync("/Admin/Produtos");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "/conta/entrar?ReturnUrl=",
            response.Headers.Location?.PathAndQuery,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductPageSelectsVariantAndEmitsCanonicalMetadata()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "ambar-seo");
        using var client = application.CreateClient();

        using var response = await client.GetAsync(
            $"/perfumes/{product.Slug}?variante={product.OutOfStockVariantId}");
        var content = await response.Content.ReadAsStringAsync();
        var decodedContent = WebUtility.HtmlDecode(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "<title>Âmbar Noturno | Sallvat & Co.</title>",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            $"<link rel=\"canonical\" href=\"https://tests.sallvat.invalid/perfumes/{product.Slug}\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<meta property=\"og:type\" content=\"product\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<meta property=\"og:image\" content=\"https://tests.sallvat.invalid/media/",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<meta property=\"og:image:width\" content=\"800\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<meta property=\"og:image:height\" content=\"1000\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<script type=\"application/ld",
            content,
            StringComparison.Ordinal);
        Assert.Contains("\"@type\":\"Product\"", content, StringComparison.Ordinal);
        Assert.Contains("\"@type\":\"Offer\"", content, StringComparison.Ordinal);
        Assert.Contains(
            "https://schema.org/OutOfStock",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            $"href=\"/perfumes/{product.Slug}?variante={product.OutOfStockVariantId}\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "aria-current=\"true\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "Esta variante está temporariamente esgotada.",
            decodedContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldSlugRedirectsAndArchivedProductReturnsNotFound()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "ambar-arquivado");
        using (var scope = application.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var current = await service.GetAdminAsync(product.ProductId);
            var updated = await service.UpdateProductAsync(
                product.ProductId,
                current!.ConcurrencyVersion,
                PublishedCatalogFixture.ProductInput("ambar-atual"),
                new AdminOperationContext(product.ActorId, "slug-web-test"));
            Assert.True(updated.Succeeded);
        }

        using var client = application.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        using var oldSlugResponse = await client.GetAsync(
            $"/perfumes/{product.Slug}");
        Assert.Equal(HttpStatusCode.MovedPermanently, oldSlugResponse.StatusCode);
        Assert.Equal(
            "/perfumes/ambar-atual",
            oldSlugResponse.Headers.Location?.OriginalString);

        using (var scope = application.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var current = await service.GetAdminAsync(product.ProductId);
            var archived = await service.ArchiveAsync(
                product.ProductId,
                current!.ConcurrencyVersion,
                new AdminOperationContext(product.ActorId, "archive-web-test"));
            Assert.True(archived.Succeeded);
        }

        using var response = await client.GetAsync(
            "/perfumes/ambar-atual");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
