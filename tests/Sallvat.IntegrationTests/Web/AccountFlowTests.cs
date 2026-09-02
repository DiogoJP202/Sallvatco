using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Accounts;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.IntegrationTests.Web;

public sealed partial class AccountFlowTests
{
    private const string ValidPassword = "Segura#2026Perfume";

    [Fact]
    public async Task RegistrationConfirmationAndLoginCompleteTheAccountFlow()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);

        var registerToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/criar");
        using var registerResponse = await client.PostAsync(
            "/conta/criar",
            Form(
                registerToken,
                ("Name", "Cliente Teste"),
                ("Email", "cliente@example.com"),
                ("Phone", "+55 11 99999-0000"),
                ("Password", ValidPassword),
                ("ConfirmPassword", ValidPassword),
                ("AcceptTerms", "true")));

        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);
        Assert.Equal(
            "/conta/verifique-seu-email",
            registerResponse.Headers.Location?.OriginalString);
        var confirmation = Assert.Single(application.EmailSender.Deliveries);
        Assert.False(confirmation.IsPasswordReset);
        Assert.Equal("cliente@example.com", confirmation.RecipientEmail);

        using var confirmResponse = await client.GetAsync(
            confirmation.ActionUrl.PathAndQuery);
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Contains("Seu e-mail foi confirmado", confirmContent, StringComparison.Ordinal);

        var loginToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/entrar");
        using var loginResponse = await client.PostAsync(
            "/conta/entrar",
            Form(
                loginToken,
                ("Email", "cliente@example.com"),
                ("Password", ValidPassword),
                ("RememberMe", "false"),
                ("ReturnUrl", "/conta/enderecos")));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal(
            "/conta/enderecos",
            loginResponse.Headers.Location?.OriginalString);

        using var accountResponse = await client.GetAsync("/conta");
        var accountContent = await accountResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        Assert.Contains("Cliente Teste", accountContent, StringComparison.Ordinal);
        Assert.Contains("E-mail confirmado", accountContent, StringComparison.Ordinal);

        using var scope = application.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var user = await userManager.FindByEmailAsync("cliente@example.com");
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Single(context.Customers, customer =>
            customer.ApplicationUserId == user.Id);
    }

    [Fact]
    public async Task AccountPostsRejectRequestsWithoutAntiforgeryToken()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);

        using var response = await client.PostAsync(
            "/conta/criar",
            Form(
                null,
                ("Name", "Cliente Teste"),
                ("Email", "cliente@example.com"),
                ("Password", ValidPassword),
                ("ConfirmPassword", ValidPassword),
                ("AcceptTerms", "true")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmedAccountCanResetItsPasswordWithoutEmailEnumeration()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);
        var registrationToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/criar");
        using var registrationResponse = await client.PostAsync(
            "/conta/criar",
            RegistrationForm(registrationToken));
        var confirmation = Assert.Single(application.EmailSender.Deliveries);
        using var confirmationResponse = await client.GetAsync(
            confirmation.ActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);

        var recoveryToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/esqueci-minha-senha");
        using var knownEmailResponse = await client.PostAsync(
            "/conta/esqueci-minha-senha",
            Form(recoveryToken, ("Email", "cliente@example.com")));
        using var unknownEmailResponse = await client.PostAsync(
            "/conta/esqueci-minha-senha",
            Form(recoveryToken, ("Email", "desconhecido@example.com")));

        Assert.Equal(HttpStatusCode.Redirect, knownEmailResponse.StatusCode);
        Assert.Equal(
            knownEmailResponse.Headers.Location,
            unknownEmailResponse.Headers.Location);
        var reset = Assert.Single(
            application.EmailSender.Deliveries,
            delivery => delivery.IsPasswordReset);
        var query = QueryHelpers.ParseQuery(reset.ActionUrl.Query);
        var resetFormToken = await GetAntiforgeryTokenAsync(
            client,
            reset.ActionUrl.PathAndQuery);
        const string newPassword = "Nova#2026Fragrancia";

        using var resetResponse = await client.PostAsync(
            "/conta/redefinir-senha",
            Form(
                resetFormToken,
                ("UserId", query["userId"].ToString()),
                ("Code", query["code"].ToString()),
                ("Password", newPassword),
                ("ConfirmPassword", newPassword)));
        Assert.Equal(HttpStatusCode.Redirect, resetResponse.StatusCode);
        Assert.Equal(
            "/conta/senha-redefinida",
            resetResponse.Headers.Location?.OriginalString);

        var loginToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/entrar");
        using var loginResponse = await client.PostAsync(
            "/conta/entrar",
            Form(
                loginToken,
                ("Email", "cliente@example.com"),
                ("Password", newPassword),
                ("RememberMe", "false")));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    [Fact]
    public async Task RegistrationRateLimitRejectsTheSixthPostFromAnIp()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client, "/conta/criar");

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var accepted = await client.PostAsync(
                "/conta/criar",
                RegistrationForm(token));
            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                accepted.StatusCode);
        }

        using var rejected = await client.PostAsync(
            "/conta/criar",
            RegistrationForm(token));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task FiveInvalidPasswordsLockTheAccountForFurtherAttempts()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var client = CreateClient(application);
        var registrationToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/criar");
        using var registrationResponse = await client.PostAsync(
            "/conta/criar",
            RegistrationForm(registrationToken));
        var confirmation = Assert.Single(application.EmailSender.Deliveries);
        using var confirmationResponse = await client.GetAsync(
            confirmation.ActionUrl.PathAndQuery);
        var loginToken = await GetAntiforgeryTokenAsync(
            client,
            "/conta/entrar");
        string responseContent = string.Empty;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsync(
                "/conta/entrar",
                Form(
                    loginToken,
                    ("Email", "cliente@example.com"),
                    ("Password", "Senha#Incorreta2026"),
                    ("RememberMe", "false")));
            responseContent = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Contains(
            "Acesso temporariamente bloqueado",
            responseContent,
            StringComparison.Ordinal);

        using var blockedResponse = await client.PostAsync(
            "/conta/entrar",
            Form(
                loginToken,
                ("Email", "cliente@example.com"),
                ("Password", ValidPassword),
                ("RememberMe", "false")));
        var blockedContent = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, blockedResponse.StatusCode);
        Assert.Contains(
            "Acesso temporariamente bloqueado",
            blockedContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddressQueriesAreAlwaysScopedToTheCurrentUser()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        using var scope = application.Services.CreateScope();
        var accounts = scope.ServiceProvider
            .GetRequiredService<IAccountService>();
        var first = await accounts.RegisterAsync(new RegisterAccountCommand(
            "Primeiro Cliente",
            "primeiro@example.com",
            null,
            ValidPassword));
        var second = await accounts.RegisterAsync(new RegisterAccountCommand(
            "Segundo Cliente",
            "segundo@example.com",
            null,
            ValidPassword));
        var firstUserId = first.EmailChallenge!.UserId;
        var secondUserId = second.EmailChallenge!.UserId;
        var address = new AddressInput(
            "Casa",
            "Primeiro Cliente",
            "01310-100",
            "Avenida Paulista",
            "1000",
            null,
            "Bela Vista",
            "São Paulo",
            "SP");

        var created = await accounts.CreateAddressAsync(
            firstUserId,
            address);
        var firstAddresses = await accounts.ListAddressesAsync(firstUserId);
        var firstAddress = Assert.Single(firstAddresses);
        var leakedAddress = await accounts.GetAddressAsync(
            secondUserId,
            firstAddress.Id);
        var unauthorizedUpdate = await accounts.UpdateAddressAsync(
            secondUserId,
            firstAddress.Id,
            address);

        Assert.True(created.Succeeded);
        Assert.Null(leakedAddress);
        Assert.False(unauthorizedUpdate.Succeeded);
    }

    private static HttpClient CreateClient(
        AccountWebApplicationFactory application) =>
        application.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
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

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent RegistrationForm(string token) =>
        Form(
            token,
            ("Name", "Cliente Teste"),
            ("Email", "cliente@example.com"),
            ("Password", ValidPassword),
            ("ConfirmPassword", ValidPassword),
            ("AcceptTerms", "true"));

    private static FormUrlEncodedContent Form(
        string? antiforgeryToken,
        params (string Key, string Value)[] values)
    {
        var fields = values
            .Select(value => new KeyValuePair<string, string>(
                value.Key,
                value.Value))
            .ToList();
        if (antiforgeryToken is not null)
        {
            fields.Add(new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                antiforgeryToken));
        }

        return new FormUrlEncodedContent(fields);
    }

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
