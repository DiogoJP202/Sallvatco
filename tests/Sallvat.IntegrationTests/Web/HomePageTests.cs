using System.Net;
using Sallvat.IntegrationTests.Catalog;

namespace Sallvat.IntegrationTests.Web;

public sealed class HomePageTests
{
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
