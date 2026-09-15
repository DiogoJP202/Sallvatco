using System.Net;
using Sallvat.IntegrationTests.Catalog;

namespace Sallvat.IntegrationTests.Web;

public sealed class HomePageTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/sobre")]
    [InlineData("/linha-corporal")]
    public async Task PublicPagesOmitImageProductionNotes(string route)
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = application.CreateClient();

        var content = WebUtility.HtmlDecode(await client.GetStringAsync(route));

        Assert.DoesNotContain("imagem tratada", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("imagens tratadas", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tratadas com IA", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noindex,nofollow", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AboutPageUsesProvisionalBrandNarrativeAndWebpAssets()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/sobre");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Uma história começa antes da primeira nota.",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "ainda estão em processo de curadoria e aprovação",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "/images/showcase/atmosphere-hibernum.webp?v=",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<link rel=\"canonical\" href=\"https://tests.sallvat.invalid/sobre\"",
            content,
            StringComparison.Ordinal);

        using var brandImage = await client.GetAsync(
            "/images/showcase/atmosphere-hibernum.webp");
        Assert.Equal(HttpStatusCode.OK, brandImage.StatusCode);
        Assert.Equal(
            "image/webp",
            brandImage.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HomeRendersAccessibleAccountLaunchExperience()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"pt-BR\"", content, StringComparison.Ordinal);
        Assert.Contains("href=\"#conteudo\"", content, StringComparison.Ordinal);
        Assert.Contains("<main id=\"conteudo\"", content, StringComparison.Ordinal);
        Assert.Contains("/css/app.css?v=", content, StringComparison.Ordinal);
        Assert.Contains(
            "Toda fragrância começa com uma história.",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "/images/home/hero-coastal.webp?v=",
            content,
            StringComparison.Ordinal);
        Assert.Contains("Conheça o catálogo", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/perfumes\"", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/conta/criar\"", content, StringComparison.Ordinal);
        Assert.Contains("data-menu-button", content, StringComparison.Ordinal);
        Assert.Contains("data-mobile-menu", content, StringComparison.Ordinal);
        Assert.Contains(
            "O cuidado em cada detalhe",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "href=\"/linha-corporal\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "/images/showcase/packshots/body-sea-salt-thumb.webp",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "/js/storefront.js?v=",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<link rel=\"canonical\" href=\"https://tests.sallvat.invalid/\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<meta name=\"robots\" content=\"noindex,nofollow\"",
            content,
            StringComparison.Ordinal);

        using var campaignImage = await client.GetAsync(
            "/images/home/hero-coastal.webp");
        Assert.Equal(HttpStatusCode.OK, campaignImage.StatusCode);
        Assert.Equal(
            "image/webp",
            campaignImage.Content.Headers.ContentType?.MediaType);

        using var storefrontScript = await client.GetAsync(
            "/js/storefront.js");
        Assert.Equal(HttpStatusCode.OK, storefrontScript.StatusCode);
        Assert.Equal(
            "text/javascript",
            storefrontScript.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BodySplashPageUsesTheNinePreparedProductImages()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/linha-corporal");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            9,
            content.Split(
                "data-body-splash-card",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "O cuidado encontra a sua fragrância.",
            content,
            StringComparison.Ordinal);
        Assert.Contains("Aqua Imagination", content, StringComparison.Ordinal);
        Assert.Contains("Golden Freesia", content, StringComparison.Ordinal);
        Assert.Contains(
            "<meta name=\"robots\" content=\"noindex,nofollow\"",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "<link rel=\"canonical\" href=\"https://tests.sallvat.invalid/linha-corporal\"",
            content,
            StringComparison.Ordinal);

        using var thumbnail = await client.GetAsync(
            "/images/showcase/packshots/body-vanilla-cream-thumb.webp");
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
        Assert.Equal(
            "image/webp",
            thumbnail.Content.Headers.ContentType?.MediaType);

        using var largeImage = await client.GetAsync(
            "/images/showcase/packshots/body-vanilla-cream.webp");
        Assert.Equal(HttpStatusCode.OK, largeImage.StatusCode);
        Assert.Equal(
            "image/webp",
            largeImage.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FeaturedProductAppearsOnHome()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "ambar-home");
        var regularProduct = await PublishedCatalogFixture.CreateAsync(
            application,
            "ambar-sem-destaque",
            featured: false);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();
        var decodedContent = WebUtility.HtmlDecode(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Perfumes em destaque", content, StringComparison.Ordinal);
        Assert.Contains(
            "Âmbar Noturno",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            $"href=\"/perfumes/{product.Slug}\"",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"href=\"/perfumes/{regularProduct.Slug}\"",
            content,
            StringComparison.Ordinal);
    }
}
