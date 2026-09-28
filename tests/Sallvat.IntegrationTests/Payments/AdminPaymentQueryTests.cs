using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Authorization;
using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class AdminPaymentQueryTests
{
    [Fact]
    public async Task ReviewOnCancelledOrderRemainsVisibleWithoutExposingSecretsOrChangingState()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        payment.RequireCanonicalReview(PaymentAttentionReason.LateApproval, PaymentDispatchTests.Now);
        (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, PaymentDispatchTests.Now);
        db.WebhookEvents.Add(new(new string('A', 64), payment.Id, "ORD-webhook", WebhookOutcome.RequiresAttention, PaymentDispatchTests.Now));
        await db.SaveChangesAsync();
        var version = payment.ConcurrencyVersion;
        db.ChangeTracker.Clear();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        var result = await query.ListAsync(AdminPaymentFilter.Attention);
        var summary = Assert.Single(result.Items);
        Assert.Equal(OrderStatus.Cancelled, summary.OrderStatus);
        Assert.Equal(PaymentAttentionReason.LateApproval, summary.AttentionReason);
        var details = Assert.IsType<AdminPaymentDetails>(await query.FindAsync(payment.Id));
        Assert.Equal(WebhookOutcome.RequiresAttention, Assert.Single(details.Receipts).Outcome);
        Assert.Equal("ORD-webhook", details.ExternalOrderId);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(version, (await db.Payments.AsNoTracking().SingleAsync()).ConcurrencyVersion);
        Assert.Empty(await db.InventoryMovements.ToListAsync());

        await using var authenticated = AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, app);
        using var client = authenticated.CreateClient();
        var queueHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Pagamentos"));
        Assert.Contains($"href=\"/Admin/Pagamentos/{payment.Id}\"", queueHtml);
        Assert.Contains("Cancelado", queueHtml);
        Assert.Contains("Sandbox (teste)", queueHtml);
        Assert.Contains("name=\"robots\" content=\"noindex,nofollow\"", queueHtml);
        Assert.Contains("Nenhuma tentativa encontrada", await client.GetStringAsync("/Admin/Pagamentos?filter=Approved"));
        using var response = await client.GetAsync($"/Admin/Pagamentos/{payment.Id}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("Cancelado", html);
        Assert.Contains("Captura observada fora das condições", html);
        Assert.Contains("Encaminhado para revisão", html);
        Assert.Contains("ORD-webhook", html);
        Assert.DoesNotContain("cliente@example.com", html);
        Assert.DoesNotContain("11999998888", html);
        Assert.DoesNotContain(payment.IdempotencyKey.ToString(), html);
        Assert.DoesNotContain(payment.DispatchToken.ToString()!, html);
        Assert.DoesNotContain(new string('A', 64), html);
        Assert.DoesNotContain("WebhookSecret", html);
        Assert.DoesNotContain("/integracoes/", html);
    }

    [Fact]
    public async Task FiltersAndKeysetPaginationDoNotLoseEqualTimestampRows()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var template = await db.Orders.SingleAsync();
        // Each attempt belongs to a distinct order, matching production uniqueness.
        for (var index = 1; index <= 30; index++)
        {
            var order = new Order(1_000 + index, $"SVT-20260921-{1_000 + index:D8}", Guid.NewGuid(), template.SourceCartId, template.CustomerId,
                "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
                "Melhor Envio", "Transportadora", "Expresso", "quote-123", 2, 4, PaymentDispatchTests.Now, PaymentDispatchTests.Now, template.ExpiresAtUtc);
            db.Orders.Add(order);
            var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), PaymentDispatchTests.Now);
            payment.TryBeginOrderDispatch(Guid.NewGuid(), PaymentDispatchTests.Now);
            db.Payments.Add(payment);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        var first = await query.ListAsync(AdminPaymentFilter.Attention);
        Assert.Equal(25, first.Items.Count);
        Assert.NotNull(first.NextBeforeId);
        var second = await query.ListAsync(AdminPaymentFilter.Attention, first.NextBeforeId);
        Assert.Equal(5, second.Items.Count);
        Assert.Null(second.NextBeforeId);
        Assert.Equal(30, first.Items.Concat(second.Items).Select(p => p.Id).Distinct().Count());
        Assert.All(first.Items.Concat(second.Items), p => Assert.Equal(PaymentDispatchState.Sending, p.DispatchState));
        Assert.Single((await query.ListAsync(AdminPaymentFilter.Pending)).Items);
        Assert.Empty((await query.ListAsync(AdminPaymentFilter.Approved)).Items);
        Assert.Equal(25, (await query.ListAsync(AdminPaymentFilter.All)).Items.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReceiptHistoryIsBoundedAndCaptureEvidenceIsDisplayedAfterReview()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        payment.ConfirmOrderPayment("PAY-confirmed", PaymentDispatchTests.Now, PaymentDispatchTests.Now);
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        Assert.Single((await query.ListAsync(AdminPaymentFilter.Approved)).Items);
        payment.RequireCanonicalReview(PaymentAttentionReason.FinancialReview, PaymentDispatchTests.Now);
        for (var index = 0; index < 51; index++)
        {
            db.WebhookEvents.Add(new(index.ToString("X64", System.Globalization.CultureInfo.InvariantCulture), payment.Id, "ORD-webhook", WebhookOutcome.Observed, PaymentDispatchTests.Now.AddSeconds(index)));
        }

        await db.SaveChangesAsync();
        var details = Assert.IsType<AdminPaymentDetails>(await query.FindAsync(payment.Id));
        Assert.Equal("PAY-confirmed", details.ExternalPaymentId);
        Assert.NotNull(details.ConfirmedAtUtc);
        Assert.Equal(50, details.Receipts.Count);
        Assert.True(details.HasOlderReceipts);
        Assert.Equal(PaymentDispatchTests.Now.AddSeconds(50), details.Receipts[0].ReceivedAtUtc);
        Assert.Equal(PaymentDispatchTests.Now.AddSeconds(1), details.Receipts[^1].ReceivedAtUtc);
        Assert.Empty((await query.ListAsync(AdminPaymentFilter.Approved)).Items);
    }

    [Fact]
    public async Task MissingExternalIdIsVisibleWithoutCreatingOrAssociatingAnything()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentPreparationTests.SeedAsync(db);
        var payment = new Payment(await db.Orders.SingleAsync(), PaymentEnvironment.Sandbox, Guid.NewGuid(), PaymentDispatchTests.Now);
        var token = Guid.NewGuid();
        payment.TryBeginOrderDispatch(token, PaymentDispatchTests.Now);
        payment.CompleteOrderDispatch(token, null, false, PaymentDispatchTests.Now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await using var authenticated = AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, app);
        using var client = authenticated.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Pagamentos/{payment.Id}"));
        Assert.Contains("Não registrado — não reenviar automaticamente", html);
        Assert.Contains("Resultado do envio Orders desconhecido", html);
        Assert.Contains("Nenhum recibo processado registrado", html);
        Assert.Null((await db.Payments.AsNoTracking().SingleAsync()).ExternalOrderId);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
    }

    [Theory]
    [InlineData("?filter=Invalid")]
    [InlineData("?filter=999")]
    [InlineData("?beforeId=0")]
    [InlineData("?beforeId=-1")]
    [InlineData("?beforeId=oops")]
    public async Task InvalidQueryIsBadRequest(string query)
    {
        await using var app = new AccountWebApplicationFactory();
        await using var authenticated = AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, app);
        using var client = authenticated.CreateClient();
        using var response = await client.GetAsync("/Admin/Pagamentos" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyQueueMissingAttemptAndPostAreHandledWithoutMutation()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        await using var authenticated = AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, app);
        using var client = authenticated.CreateClient();
        using var response = await client.GetAsync("/Admin/Pagamentos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("Nenhuma tentativa encontrada", await response.Content.ReadAsStringAsync());
        using var missing = await client.GetAsync("/Admin/Pagamentos/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var post = await client.PostAsync("/Admin/Pagamentos/999", new StringContent(""));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        var home = await client.GetStringAsync("/Admin");
        Assert.Contains("href=\"/Admin/Pagamentos\"", home);
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync()
    {
        var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        await PaymentWebhookTests.SeedAsync(scope.ServiceProvider.GetRequiredService<SallvatDbContext>());
        return app;
    }
}
