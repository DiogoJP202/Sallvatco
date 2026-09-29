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

public sealed class PaymentRecoveryFollowUpTests
{
    [Theory]
    [InlineData(0, false, AdminRecoveryFollowUp.None)]
    [InlineData(2, false, AdminRecoveryFollowUp.None)]
    [InlineData(3, false, AdminRecoveryFollowUp.AttemptsExhausted)]
    [InlineData(0, true, AdminRecoveryFollowUp.WindowExpired)]
    [InlineData(3, true, AdminRecoveryFollowUp.AttemptsExhausted | AdminRecoveryFollowUp.WindowExpired)]
    public async Task TypedReasonsUseDurableBudgetAndExactWindowWithoutMutation(int count, bool expired, AdminRecoveryFollowUp expected)
    {
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(24).AddTicks(expired ? 1 : 0) };
        await using var app = await CreateAsync(clock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        for (var index = 0; index < count; index++) { AddExecution(db, payment.Id, index, PaymentRecoverySource.Automatic); }
        // Human queries never consume the automatic budget.
        for (var index = 0; index < 4; index++) { AddExecution(db, payment.Id, index + 10, PaymentRecoverySource.Manual); }
        await db.SaveChangesAsync();
        var version = payment.ConcurrencyVersion;
        db.ChangeTracker.Clear();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        var result = await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp);
        var attention = await query.ListAsync(AdminPaymentFilter.Attention);
        if (expected == AdminRecoveryFollowUp.None)
        {
            Assert.Empty(result.Items);
            Assert.Empty(attention.Items);
        }
        else
        {
            Assert.Equal(expected, Assert.Single(result.Items).RecoveryFollowUp);
            Assert.Equal(expected, Assert.Single(attention.Items).RecoveryFollowUp);
        }
        Assert.Equal(expected, Assert.Single((await query.ListAsync(AdminPaymentFilter.Pending)).Items).RecoveryFollowUp);
        var detail = Assert.IsType<AdminPaymentDetails>(await query.FindAsync(payment.Id));
        Assert.Equal(expected, detail.Payment.RecoveryFollowUp);
        Assert.False(detail.AutomaticRecoveryEnabled);
        Assert.False(detail.CanRecover);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(version, (await db.Payments.AsNoTracking().SingleAsync()).ConcurrencyVersion);
        Assert.Equal(count + 4, await db.PaymentRecoveryExecutions.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Theory]
    [InlineData(PaymentRecoverySource.Manual)]
    [InlineData(PaymentRecoverySource.Automatic)]
    public async Task ActiveConsultationSuppressesSignalUntilExactLeaseExpiry(PaymentRecoverySource source)
    {
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(25) };
        await using var app = await CreateAsync(clock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        db.PaymentRecoveryExecutions.Add(new(Guid.NewGuid(), payment.Id, clock.UtcNow, source));
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        Assert.Empty((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items);
        Assert.Equal(AdminRecoveryFollowUp.None, (await query.FindAsync(payment.Id))!.Payment.RecoveryFollowUp);
        clock.UtcNow += PaymentRecoveryExecution.Lifetime;
        Assert.Equal(AdminRecoveryFollowUp.WindowExpired, Assert.Single((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items).RecoveryFollowUp);
        Assert.Equal(PaymentRecoveryExecutionState.Running, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("review")]
    [InlineData("missing")]
    [InlineData("production")]
    public async Task OtherFinancialPathsDoNotMasqueradeAsAutomaticExhaustion(string kind)
    {
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(25) };
        await using var app = await CreateAsync(clock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        if (kind == "approved") { payment.ConfirmOrderPayment("PAY-confirmed", PaymentDispatchTests.Now, PaymentDispatchTests.Now); }
        else if (kind == "review") { payment.RequireCanonicalReview(PaymentAttentionReason.FinancialReview, clock.UtcNow); }
        else if (kind == "production") { db.Entry(payment).Property(p => p.Environment).CurrentValue = PaymentEnvironment.Production; }
        else { db.Entry(payment).Property(p => p.ExternalOrderId).CurrentValue = null; }
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        Assert.Empty((await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp)).Items);
        Assert.Equal(AdminRecoveryFollowUp.None, (await query.FindAsync(payment.Id))!.Payment.RecoveryFollowUp);
        if (kind == "review") { Assert.Single((await query.ListAsync(AdminPaymentFilter.Attention)).Items); }
    }

    [Fact]
    public async Task FollowUpPaginationKeepsCancelledOrdersAndHttpFilterWithoutExposingSecrets()
    {
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(25) };
        await using var app = await CreateAsync(clock);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var template = await db.Orders.SingleAsync();
        for (var index = 1; index <= 30; index++)
        {
            var order = new Order(1_000 + index, $"SVT-20260921-{1_000 + index:D8}", Guid.NewGuid(), template.SourceCartId, template.CustomerId,
                "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
                "Melhor Envio", "Transportadora", "Expresso", "quote-123", 2, 4, PaymentDispatchTests.Now, PaymentDispatchTests.Now, template.ExpiresAtUtc);
            var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), PaymentDispatchTests.Now);
            var token = Guid.NewGuid();
            payment.TryBeginOrderDispatch(token, PaymentDispatchTests.Now);
            payment.CompleteOrderDispatch(token, $"ORD-followup-{index}", true, PaymentDispatchTests.Now);
            order.TransitionTo(OrderStatus.Cancelled, clock.UtcNow);
            db.Orders.Add(order);
            db.Payments.Add(payment);
        }
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<IAdminPaymentQuery>();
        var first = await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp);
        Assert.Equal(25, first.Items.Count);
        Assert.All(first.Items, p => Assert.Equal(OrderStatus.Cancelled, p.OrderStatus));
        var second = await query.ListAsync(AdminPaymentFilter.RecoveryFollowUp, first.NextBeforeId);
        Assert.Equal(6, second.Items.Count);
        Assert.Null(second.NextBeforeId);
        Assert.Equal(31, first.Items.Concat(second.Items).Select(p => p.Id).Distinct().Count());
        await using var authenticated = AdminAuthorizationTests.CreateAuthenticatedApplication(RoleNames.Admin, app);
        using var client = authenticated.CreateClient();
        using var response = await client.GetAsync("/Admin/Pagamentos?filter=RecoveryFollowUp");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains($"filter=RecoveryFollowUp&beforeId={first.NextBeforeId}", html);
        Assert.Contains("Janela de 24 horas", html);
        Assert.Contains("Cancelado", html);
        Assert.Contains("noindex,nofollow", html);
        Assert.DoesNotContain("cliente@example.com", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Pagamentos/{first.Items[0].Id}"));
        Assert.Contains("Conferência manual necessária", html);
        Assert.Contains("Não recrie cobrança", html);
        Assert.DoesNotContain("name=\"Confirm\"", html);
        Assert.Empty(await db.PaymentRecoveryExecutions.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    private static void AddExecution(SallvatDbContext db, long id, int minute, PaymentRecoverySource source)
    {
        var now = PaymentDispatchTests.Now.AddMinutes(minute);
        var execution = new PaymentRecoveryExecution(Guid.NewGuid(), id, now, source);
        execution.Complete(now, PaymentRecoveryOutcome.Unavailable);
        db.PaymentRecoveryExecutions.Add(execution);
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync(PaymentDispatchTests.Clock clock)
    {
        var app = new AccountWebApplicationFactory(clock: clock);
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        await PaymentWebhookTests.SeedAsync(scope.ServiceProvider.GetRequiredService<SallvatDbContext>());
        return app;
    }
}
