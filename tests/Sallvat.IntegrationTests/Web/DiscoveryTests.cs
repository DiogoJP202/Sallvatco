using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Catalog;
using Sallvat.IntegrationTests.Catalog;

namespace Sallvat.IntegrationTests.Web;

public sealed partial class DiscoveryTests
{
    private const string Origin = "https://store.sallvat.example.com";
    private const string FeedPath = "/feeds/google-merchant.xml";
    private static readonly string[] ExpectedSitemapPaths =
        ["/", "/sobre", "/contato", "/entrega", "/perfumes", "/perfumes/seo-piloto"];

    [Theory]
    [InlineData("Development", true, false)]
    [InlineData("Testing", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, true)]
    public async Task IndexingAndDiscoveryRequireProductionAndExplicitApproval(
        string environment,
        bool enabled,
        bool indexable)
    {
        await using var app = CreateApplication(environment, enabled);
        await app.InitializeDatabaseAsync();
        using var client = CreateClient(app);
        using var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        var robots = await client.GetStringAsync("/robots.txt");
        using var sitemap = await client.GetAsync("/sitemap.xml");
        using var feed = await client.GetAsync(FeedPath);

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(!indexable, html.Contains("name=\"robots\" content=\"noindex", StringComparison.Ordinal));
        Assert.Equal(indexable ? HttpStatusCode.OK : HttpStatusCode.NotFound, sitemap.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, feed.StatusCode);
        Assert.Equal(indexable, robots.Contains("Sitemap: " + Origin + "/sitemap.xml", StringComparison.Ordinal));
        if (environment == "Production")
        {
            Assert.Contains("User-agent: GPTBot\nDisallow: /", robots, StringComparison.Ordinal);
            Assert.DoesNotContain("Google-Extended", robots, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("User-agent: *\nDisallow: /\n", robots);
        }

        Assert.Contains("noindex,nofollow", await client.GetStringAsync("/linha-corporal"), StringComparison.Ordinal);
        Assert.Contains("noindex,nofollow", await client.GetStringAsync("/conta/entrar"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://vitrine.github.io")]
    [InlineData("https://localhost")]
    [InlineData("https://loja.invalid")]
    public async Task ProductionIndexingRejectsNonStoreOrigins(string origin)
    {
        await using var app = new AccountWebApplicationFactory(environment: "Production", configuration:
            new Dictionary<string, string?>
            {
                ["AccountLinks:PublicOrigin"] = origin,
                ["Seo:IndexingEnabled"] = "true",
            });
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(app.InitializeDatabaseAsync);
    }

    [Fact]
    public async Task PaginationAndVariantContractsKeepOnlyValidSelections()
    {
        await using var app = CreateApplication("Production", enabled: true);
        await app.InitializeDatabaseAsync();
        PublishedProductData? first = null;
        for (var index = 1; index <= 13; index++)
        {
            var product = await PublishedCatalogFixture.CreateAsync(app,
                FormattableString.Invariant($"seo-{index}"), imageWidth: 16, imageHeight: 20);
            first ??= product;
        }

        using var client = CreateClient(app);
        var secondPage = await client.GetStringAsync("/perfumes?pagina=2&utm_source=qa");
        Assert.Contains($"href=\"{Origin}/perfumes?pagina=2\"", secondPage, StringComparison.Ordinal);
        Assert.DoesNotContain("noindex", secondPage, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", secondPage, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/perfumes?pagina=1\"", secondPage, StringComparison.Ordinal);

        var filter = await client.GetStringAsync("/perfumes?familia=Amadeirado&pagina=2");
        Assert.Contains($"href=\"{Origin}/perfumes?familia=Amadeirado&amp;pagina=2\"", filter, StringComparison.Ordinal);
        Assert.Contains("noindex,nofollow", filter, StringComparison.Ordinal);

        using var alias = await client.GetAsync("/Perfumes/?pagina=1&familia=amadeirado&utm_source=qa");
        Assert.Equal(HttpStatusCode.MovedPermanently, alias.StatusCode);
        Assert.Equal("/perfumes?familia=Amadeirado&utm_source=qa", alias.Headers.Location?.OriginalString);
        using var homeAlias = await client.GetAsync("/Home/Index?utm_source=qa");
        Assert.Equal(HttpStatusCode.MovedPermanently, homeAlias.StatusCode);
        Assert.Equal("/?utm_source=qa", homeAlias.Headers.Location?.OriginalString);

        foreach (var path in new[]
        {
            "/perfumes?pagina=3", "/perfumes?pagina=0", "/perfumes?pagina=-1",
            "/perfumes?pagina=abc", "/perfumes?pagina=", "/perfumes?pagina=1&pagina=2",
            "/perfumes?familia=desconhecida", "/perfumes?familia=",
            "/perfumes?familia=Amadeirado&familia=Amadeirado",
            "/perfumes/seo-1?variante=abc", "/perfumes/seo-1?variante=99999",
            "/perfumes/seo-1?variante=1&variante=2",
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Explorar perfumes", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using var variantAlias = await client.GetAsync(
            FormattableString.Invariant($"/PERFUMES/SEO-1/?variante={first!.OutOfStockVariantId}&gclid=qa"));
        Assert.Equal(HttpStatusCode.MovedPermanently, variantAlias.StatusCode);
        Assert.Equal(FormattableString.Invariant($"/perfumes/seo-1?variante={first.OutOfStockVariantId}&gclid=qa"),
            variantAlias.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SitemapAndMerchantFeedMatchThePublicProductAndUseStableIdentifiers()
    {
        await using var app = CreateApplication("Production", enabled: true, merchant: true);
        await app.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(app, "seo-piloto", imageWidth: 16, imageHeight: 20);
        var privateProduct = await PublishedCatalogFixture.CreateAsync(app, "seo-arquivado", imageWidth: 16, imageHeight: 20);
        using var scope = app.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var archived = await catalog.GetAdminAsync(privateProduct.ProductId);
        Assert.True((await catalog.ArchiveAsync(privateProduct.ProductId, archived!.ConcurrencyVersion,
            new AdminOperationContext(privateProduct.ActorId, "seo-archive"))).Succeeded);
        using var client = CreateClient(app);

        using var sitemap = await client.GetAsync("/sitemap.xml");
        var document = XDocument.Parse(await sitemap.Content.ReadAsStringAsync());
        XNamespace sitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = document.Descendants(sitemapNamespace + "loc").Select(node => node.Value).ToArray();
        Assert.Equal(ExpectedSitemapPaths.Select(path => Origin + path), locations);
        Assert.Empty(document.Descendants(sitemapNamespace + "lastmod"));
        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, "/sitemap.xml");
        conditionalRequest.Headers.IfNoneMatch.Add(sitemap.Headers.ETag!);
        using var unchanged = await client.SendAsync(conditionalRequest);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        using var feedResponse = await client.GetAsync(FeedPath);
        Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);
        Assert.True(feedResponse.Headers.CacheControl?.Private);
        XNamespace g = "http://base.google.com/ns/1.0";
        var items = XDocument.Parse(await feedResponse.Content.ReadAsStringAsync()).Descendants("item").ToArray();
        Assert.Equal(2, items.Length);
        foreach (var (item, variantId, price, availability) in new[]
        {
            (items[0], product.AvailableVariantId, "299.90", "in_stock"),
            (items[1], product.OutOfStockVariantId, "449.90", "out_of_stock"),
        })
        {
            var landing = FormattableString.Invariant($"/perfumes/seo-piloto?variante={variantId}");
            Assert.Equal(Origin + landing, item.Element(g + "link")?.Value);
            Assert.Equal(Origin + "/perfumes/seo-piloto", item.Element(g + "canonical_link")?.Value);
            Assert.Equal(FormattableString.Invariant($"sallvat-v-{variantId}"), item.Element(g + "id")?.Value);
            Assert.Equal(price + " BRL", item.Element(g + "price")?.Value);
            Assert.Equal(availability, item.Element(g + "availability")?.Value);
            Assert.Equal("no", item.Element(g + "identifier_exists")?.Value);
            var html = await client.GetStringAsync(landing);
            Assert.DoesNotContain("Valores e estoque ilustrativos", html, StringComparison.Ordinal);
            var jsonLd = JsonLd().Match(html);
            Assert.True(jsonLd.Success, "The product page did not render a JSON-LD script.");
            using var data = JsonDocument.Parse(jsonLd.Groups[1].Value);
            var group = data.RootElement.GetProperty("@graph").EnumerateArray()
                .Single(entity => entity.GetProperty("@type").GetString() == "ProductGroup");
            var offer = group.GetProperty("hasVariant").EnumerateArray()
                .Select(variant => variant.GetProperty("offers"))
                .Single(variantOffer => variantOffer.GetProperty("url").GetString() == Origin + landing);
            Assert.Equal(price, offer.GetProperty("price").GetRawText());
            Assert.Equal(availability == "in_stock" ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
                offer.GetProperty("availability").GetString());
        }
    }

    private static AccountWebApplicationFactory CreateApplication(string environment, bool enabled, bool merchant = false) =>
        new(environment: environment, configuration: new Dictionary<string, string?>
        {
            ["AccountLinks:PublicOrigin"] = Origin,
            ["Seo:IndexingEnabled"] = enabled ? "true" : "false",
            ["Seo:MerchantFeedEnabled"] = merchant ? "true" : "false",
            ["Seo:MerchantItems:1:NoAssignedIdentifiers"] = "true",
            ["Seo:MerchantItems:2:NoAssignedIdentifiers"] = "true",
        });

    private static HttpClient CreateClient(AccountWebApplicationFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(Origin),
        });

    [GeneratedRegex("<script\\b[^>]*type=\"application/ld(?:\\+|&#x2B;)json\"[^>]*>\\s*(\\{[^<]+\\})\\s*</script>")]
    private static partial Regex JsonLd();
}
