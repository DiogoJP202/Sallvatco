using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sallvat.Application.Authorization;
using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed partial class PaymentRefundWebTests
{
    [Fact]
    public async Task AdminPreparesThroughHttpAndReadsImmutableLocalIntentionWithoutExternalCalls()
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var (payment, order) = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}");
        Assert.Contains("/PrepararReembolso", html);
        Assert.Equal(0, gateway.Calls);
        var fields = Fields(Token(html), payment, order);
        fields["Amount"] = "0.01";
        fields["ActorUserId"] = Guid.NewGuid().ToString();
        using var post = await client.PostAsync(Route(payment), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        using var result = await client.GetAsync(post.Headers.Location);
        Assert.True(result.Headers.CacheControl?.NoStore);
        html = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());
        Assert.Contains("não enviado", html);
        Assert.DoesNotContain("action=\"" + Route(payment), html);
        Assert.DoesNotContain("cliente@example.com", html);
        Assert.DoesNotContain(payment.IdempotencyKey.ToString(), html);
        using var replay = await client.PostAsync(Route(payment), new FormUrlEncodedContent(Fields(Token(html), payment, order)));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Pagamentos?filter=RefundPrepared"));
        Assert.Contains(order.OrderNumber, list);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var refund = await db.PaymentRefundRequests.SingleAsync();
        Assert.Equal(payment.Amount, refund.Amount);
        Assert.Equal(PaymentRecoveryTests.AdminId, refund.ActorUserId);
        Assert.Single(await db.AuditLogs.ToListAsync());
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("Confirm")]
    [InlineData("Reason")]
    [InlineData("ExpectedVersion")]
    [InlineData("ExpectedOrderVersion")]
    public async Task InvalidFormsNeverPrepareRefund(string invalid)
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var (payment, order) = await SeedAsync(app.Services);
        using var client = Client(app);
        var fields = Fields(Token(await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}")), payment, order);
        if (invalid == "csrf") { fields["__RequestVerificationToken"] = "invalid"; }
        else { fields.Remove(invalid); }
        using var response = await client.PostAsync(Route(payment), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SallvatDbContext>().PaymentRefundRequests.ToListAsync());
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData("disabled", HttpStatusCode.Redirect)]
    [InlineData("revoked", HttpStatusCode.Forbidden)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("stale", HttpStatusCode.Redirect)]
    public async Task ServerRejectsDisabledRevokedUnauthorizedAndStaleRequests(string scenario, HttpStatusCode expected)
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway, scenario != "disabled", scenario == "customer" ? RoleNames.Customer : RoleNames.Admin);
        var (payment, order) = await SeedAsync(app.Services);
        using var client = Client(app);
        var html = await client.GetStringAsync(scenario == "customer" ? "/" : $"/Admin/Pagamentos/{payment.Id}");
        if (scenario == "disabled") { Assert.DoesNotContain("/PrepararReembolso", html); }
        if (scenario == "revoked")
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            db.UserRoles.Remove(await db.UserRoles.SingleAsync());
            await db.SaveChangesAsync();
        }
        var fields = Fields(Token(html), payment, order);
        if (scenario == "stale") { fields["ExpectedOrderVersion"] = Guid.NewGuid().ToString(); }
        using var response = await client.PostAsync(Route(payment), new FormUrlEncodedContent(fields));
        Assert.Equal(expected, response.StatusCode);
        using var check = app.Services.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<SallvatDbContext>().PaymentRefundRequests.ToListAsync());
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task GetCannotMutateAndSharedLimitBoundsAdminPosts()
    {
        await using var root = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new PaymentWebhookTests.Gateway();
        await using var app = Configure(root, gateway);
        var (payment, order) = await SeedAsync(app.Services);
        using var client = Client(app);
        using var get = await client.GetAsync(Route(payment));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        var token = Token(await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}"));
        for (var index = 0; index < 6; index++)
        {
            using var response = await client.PostAsync(Route(payment), new FormUrlEncodedContent(Fields(token, payment, order)));
            Assert.Equal(index < 5 ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(0, gateway.Calls);
    }

    private static string Route(Payment payment) => $"/Admin/Pagamentos/{payment.Id}/PrepararReembolso";
    private static WebApplicationFactory<Program> Configure(AccountWebApplicationFactory root, PaymentWebhookTests.Gateway gateway,
        bool enabled = true, string role = RoleNames.Admin) =>
        AdminAuthorizationTests.CreateAuthenticatedApplication(role, root, PaymentRecoveryTests.AdminId)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(gateway);
                services.PostConfigure<MercadoPagoOptions>(options =>
                {
                    options.OrdersEnabled = true;
                    options.WebhookEnabled = true;
                    options.WebhookSecret = PaymentWebhookTests.Secret;
                    options.RefundPreparationEnabled = enabled;
                    options.AccessToken = "test-only";
                    options.TestSellerConfirmed = true;
                    options.TestSellerId = 123;
                    options.PublicOrigin = "https://staging.example.com";
                });
            }));

    private static async Task<(Payment Payment, Order Order)> SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await db.Database.EnsureCreatedAsync();
        return await PaymentRefundPreparationTests.SeedAsync(db);
    }

    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static Dictionary<string, string> Fields(string token, Payment payment, Order order) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["ExpectedVersion"] = payment.ConcurrencyVersion.ToString(),
        ["ExpectedOrderVersion"] = order.ConcurrencyVersion.ToString(),
        ["Confirm"] = "true",
        ["Reason"] = "CustomerRequest",
    };

    private static string Token(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();
}
