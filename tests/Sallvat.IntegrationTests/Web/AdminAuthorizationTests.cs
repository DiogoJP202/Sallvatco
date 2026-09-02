using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sallvat.Application.Authorization;

namespace Sallvat.IntegrationTests.Web;

public sealed class AdminAuthorizationTests
{
    private const string TestScheme = "Sallvat.Tests.Authentication";

    [Fact]
    public async Task AnonymousVisitorIsRedirectedToLogin()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

        using var response = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "/conta/entrar?ReturnUrl=",
            response.Headers.Location?.PathAndQuery,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerIsForbiddenFromAdmin()
    {
        await using var application = CreateAuthenticatedApplication(
            RoleNames.Customer);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanAccessAdminHome()
    {
        await using var application = CreateAuthenticatedApplication(
            RoleNames.Admin);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/Admin");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Administração", content, StringComparison.Ordinal);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
        CreateAuthenticatedApplication(string role) =>
        new SallvatWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestScheme;
                        options.DefaultChallengeScheme = TestScheme;
                        options.DefaultForbidScheme = TestScheme;
                    })
                    .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                        TestScheme,
                        options => options.Role = role);
            }));

    private sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string Role { get; set; } = string.Empty;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) :
        AuthenticationHandler<TestAuthenticationOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, "admin-test@example.invalid"),
                new Claim(ClaimTypes.Role, Options.Role),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleForbiddenAsync(
            AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;

            return Task.CompletedTask;
        }
    }
}
