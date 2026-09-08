using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Catalog;

namespace Sallvat.IntegrationTests.Web;

public sealed partial class CartPageTests
{
    private const string ValidPassword = "Segura#2026Perfume";

    [Fact]
    public async Task EmptyCartHasSafeCallToCatalog()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);

        using var response = await client.GetAsync("/carrinho");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sua sacola ainda está vazia", content, StringComparison.Ordinal);
        Assert.Contains("/perfumes", content, StringComparison.Ordinal);
        Assert.Contains("noindex,nofollow", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddItemCreatesSecureGuestCookieAndServerSummary()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-web");
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(
            client,
            $"/perfumes/{product.Slug}");

        using var response = await client.PostAsync(
            "/carrinho/itens",
            Form(
                token,
                ("VariantId", product.AvailableVariantId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("Quantity", "2")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/carrinho", response.Headers.Location?.OriginalString);
        var cartCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "Sallvat.Cart.Testing=",
                StringComparison.Ordinal));
        Assert.Contains("httponly", cartCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cartCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cartCookie, StringComparison.OrdinalIgnoreCase);

        using var cartResponse = await client.GetAsync("/carrinho");
        var content = WebUtility.HtmlDecode(
            await cartResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, cartResponse.StatusCode);
        Assert.Contains("Âmbar Noturno", content, StringComparison.Ordinal);
        Assert.Contains("599,80", content, StringComparison.Ordinal);
        Assert.Contains("Quantidade", content, StringComparison.Ordinal);
        Assert.Contains("Checkout na próxima etapa", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginMergesGuestCartAndDeletesGuestCookie()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-login-merge");
        using var client = CreateClient(application);

        var productToken = await GetAntiforgeryTokenAsync(
            client,
            $"/perfumes/{product.Slug}");
        using var addResponse = await client.PostAsync(
            "/carrinho/itens",
            Form(
                productToken,
                ("VariantId", product.AvailableVariantId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("Quantity", "1")));
        Assert.Equal(HttpStatusCode.Redirect, addResponse.StatusCode);
        using var guestCartResponse = await client.GetAsync("/carrinho");
        Assert.Contains(
            "Âmbar Noturno",
            WebUtility.HtmlDecode(
                await guestCartResponse.Content.ReadAsStringAsync()),
            StringComparison.Ordinal);

        var registerToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/criar");
        using var registerResponse = await client.PostAsync(
            "/conta/criar",
            Form(
                registerToken,
                ("Name", "Cliente com Sacola"),
                ("Email", "sacola@example.com"),
                ("Password", ValidPassword),
                ("ConfirmPassword", ValidPassword),
                ("AcceptTerms", "true")));
        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);
        var confirmation = Assert.Single(application.EmailSender.Deliveries);
        using var confirmationResponse = await client.GetAsync(
            confirmation.ActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);

        var loginToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/entrar");
        using var loginResponse = await client.PostAsync(
            "/conta/entrar",
            Form(
                loginToken,
                ("Email", "sacola@example.com"),
                ("Password", ValidPassword),
                ("RememberMe", "false"),
                ("ReturnUrl", "/carrinho")));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/carrinho", loginResponse.Headers.Location?.OriginalString);
        Assert.Contains(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                    "Sallvat.Cart.Testing=",
                    StringComparison.Ordinal)
                && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        using var cartResponse = await client.GetAsync("/carrinho");
        var cartContent = WebUtility.HtmlDecode(
            await cartResponse.Content.ReadAsStringAsync());
        Assert.Contains("Âmbar Noturno", cartContent, StringComparison.Ordinal);

        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var persistedCart = Assert.Single(await context.Carts.ToListAsync());
        Assert.NotNull(persistedCart.CustomerId);
        Assert.Null(persistedCart.GuestTokenHash);
    }

    private static HttpClient CreateClient(
        AccountWebApplicationFactory application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex().Match(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(
        string token,
        params (string Name, string Value)[] values)
    {
        var fields = values.Select(value =>
            new KeyValuePair<string, string>(value.Name, value.Value))
            .Append(new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                token));

        return new FormUrlEncodedContent(fields);
    }

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
