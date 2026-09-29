using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sallvat.Application.Authorization;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed partial class PaymentRecoveryWebTests
{
    [Fact]
    public async Task RunningExecutionHidesFormUntilExpiryAndGetNeverRecoversIt()
    {
        var clock = new PaymentDispatchTests.Clock();
        await using var root = new AccountWebApplicationFactory(clock: clock);
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            db.PaymentRecoveryExecutions.Add(new(Guid.NewGuid(), payment.Id, clock.UtcNow));
            await db.SaveChangesAsync();
        }

        using var client = Client(app);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}"));
        Assert.Contains("Consulta em andamento", html);
        Assert.Contains("Controle das execuções", html);
        Assert.DoesNotContain("name=\"Confirm\"", html);
        using var blocked = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal(0, gateway.Calls);
        clock.UtcNow += PaymentRecoveryExecution.Lifetime;
        html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}"));
        Assert.Contains("name=\"Confirm\"", html);
        using var check = app.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(PaymentRecoveryExecutionState.Running, (await checkDb.PaymentRecoveryExecutions.SingleAsync()).State);
        Assert.Empty(await checkDb.AuditLogs.ToListAsync());
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task AdminConfirmsRecoveryThroughHttpAndSeesSanitizedHistoryWithoutRepeatingSale()
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        Assert.Contains($"action=\"/Admin/Pagamentos/{payment.Id}/Recuperar\"", html);
        Assert.Contains("name=\"Confirm\"", html);
        Assert.Contains(payment.ConcurrencyVersion.ToString(), html);
        Assert.Equal(0, gateway.Calls);
        using var response = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Pagamentos/{payment.Id}", response.Headers.Location?.OriginalString);
        using var result = await client.GetAsync(response.Headers.Location);
        html = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());
        Assert.True(result.Headers.CacheControl?.NoStore);
        Assert.Contains("Pagamento confirmado e estoque atualizado com auditoria", html);
        Assert.Contains("Solicitação registrada", html);
        Assert.Contains("Resultado registrado", html);
        Assert.DoesNotContain("name=\"Confirm\"", html);
        Assert.DoesNotContain("cliente@example.com", html);
        Assert.DoesNotContain(payment.IdempotencyKey.ToString(), html);
        Assert.DoesNotContain(payment.DispatchToken.ToString()!, html);
        Assert.DoesNotContain("recovery-test@example.invalid", html);
        using var replay = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(1, gateway.Calls);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("token")]
    [InlineData("confirm")]
    [InlineData("reason")]
    [InlineData("version")]
    public async Task InvalidFormOrMissingAntiforgeryNeverCallsGateway(string invalid)
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        using var response = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(
            invalid == "token" ? "invalid" : Token(html), invalid == "version" ? Guid.Empty : payment.ConcurrencyVersion,
            confirm: invalid != "confirm", reason: invalid == "reason" ? "999" : "MissingNotification"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.Calls);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SallvatDbContext>().AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task DisabledActionIsHiddenAndForgedPostCannotEnableRecovery()
    {
        await using var root = new AccountWebApplicationFactory();
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway, enabled: false);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        Assert.DoesNotContain("name=\"Confirm\"", html);
        using var response = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location));
        Assert.Contains("Recuperação desativada", html);
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousAndCustomerCannotPostRecovery(bool customer)
    {
        await using var root = new AccountWebApplicationFactory();
        await using var app = customer ? AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Customer, root) : root.WithWebHostBuilder(_ => { });
        using var client = Client(app);
        using var response = await client.PostAsync("/Admin/Pagamentos/1/Recuperar", Form("invalid", Guid.NewGuid()));
        Assert.Equal(customer ? HttpStatusCode.Forbidden : HttpStatusCode.Redirect, response.StatusCode);
        if (!customer)
        {
            Assert.StartsWith("/conta/entrar", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RevokedDatabaseRoleIsForbiddenEvenWithOldAdminCookie()
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            db.UserRoles.Remove(await db.UserRoles.SingleAsync());
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task RecoveryHasFiveRequestsPerAdminPerMinuteLimit()
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway { Result = new(PaymentOrderQueryStatus.Unavailable) };
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        for (var index = 0; index < 6; index++)
        {
            using var response = await client.PostAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
            Assert.Equal(index < 5 ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        Assert.Equal(5, gateway.Calls);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(10, await db.AuditLogs.CountAsync());
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task GetToRecoveryRouteCannotMutateAndMissingAttemptIsNotFound()
    {
        await using var root = new AccountWebApplicationFactory();
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var payment = await SeedAsync(app.Services);
        using var client = Client(app);
        using var get = await client.GetAsync($"/Admin/Pagamentos/{payment.Id}/Recuperar");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        using var post = await client.PostAsync("/Admin/Pagamentos/999999/Recuperar", Form(Token(html), payment.ConcurrencyVersion));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(0, gateway.Calls);
    }

    private static WebApplicationFactory<Program> Configure(AccountWebApplicationFactory root, PaymentWebhookTests.Gateway gateway, bool enabled = true) =>
        AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, root, PaymentRecoveryTests.AdminId)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(gateway);
                services.PostConfigure<MercadoPagoOptions>(options =>
                {
                    options.OrdersEnabled = enabled;
                    options.RecoveryEnabled = enabled;
                    options.AccessToken = "test-only";
                    options.TestSellerConfirmed = true;
                    options.TestSellerId = 123;
                    options.PublicOrigin = "https://staging.example.com";
                });
            }));

    private static async Task<Payment> SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await db.Database.EnsureCreatedAsync();
        await PaymentWebhookTests.SeedAsync(db);
        await PaymentRecoveryTests.SeedAdminAsync(db);
        return await db.Payments.SingleAsync();
    }

    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static FormUrlEncodedContent Form(string token, Guid version, bool confirm = true, string reason = "MissingNotification") => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["ExpectedVersion"] = version.ToString(),
        ["Confirm"] = confirm.ToString(),
        ["Reason"] = reason,
    });

    private static string Token(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();
}
