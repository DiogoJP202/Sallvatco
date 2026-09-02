using System.Net;
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

        using var response = await client.GetAsync("/perfumes");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Novas fragrâncias chegarão", content, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", content, StringComparison.OrdinalIgnoreCase);
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
}
