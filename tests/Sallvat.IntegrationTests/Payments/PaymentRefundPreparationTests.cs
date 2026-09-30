using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentRefundPreparationTests
{
    internal static PaymentRefundOperation Operation => new(PaymentRecoveryTests.AdminId, PaymentRefundReason.CustomerRequest, "refund-test");

    [Fact]
    public async Task PreparationIsImmutableAuditedAndDoesNotRefundOrRestock()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var (payment, order) = await SeedAsync(db);
        var result = await Service(db).PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, Operation);
        Assert.Equal(PaymentRefundPreparationStatus.Prepared, result.Status);
        var replay = await Service(db).PrepareAsync(payment.Id, Guid.NewGuid(), Guid.NewGuid(), Operation with { Reason = PaymentRefundReason.OperationalCorrection });
        Assert.Equal(PaymentRefundPreparationStatus.AlreadyPrepared, replay.Status);
        Assert.Equal(result.RequestId, replay.RequestId);
        var refund = await db.PaymentRefundRequests.SingleAsync();
        Assert.Equal(PaymentRefundRequestState.Prepared, refund.State);
        Assert.Equal(PaymentRefundReason.CustomerRequest, refund.Reason);
        Assert.Equal(payment.Amount, refund.Amount);
        Assert.Equal(payment.ExternalPaymentId, refund.ExternalPaymentId);
        Assert.Equal(payment.ExternalOrderId, refund.ExternalOrderId);
        Assert.Equal(payment.ConcurrencyVersion, refund.PaymentVersion);
        Assert.Equal(order.ConcurrencyVersion, refund.OrderVersion);
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal("payment.refund.prepared", audit.Action);
        Assert.Equal(Operation.ActorUserId, audit.ActorUserId);
        Assert.Contains(refund.Id.ToString(), audit.ChangesJson);
        Assert.DoesNotContain("cliente@example.com", audit.ChangesJson);
        Assert.DoesNotContain("ORD-webhook", audit.ChangesJson);
        Assert.DoesNotContain("PAY-webhook", audit.ChangesJson);
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Equal(payment.ConcurrencyVersion, (await db.Payments.SingleAsync()).ConcurrencyVersion);
        Assert.Equal(order.ConcurrencyVersion, (await db.Orders.SingleAsync()).ConcurrencyVersion);
        db.ChangeTracker.Clear();
        var query = new AdminPaymentQuery(db, Options.Create(Configuration()), new PaymentDispatchTests.Clock());
        Assert.True((await query.ListAsync(AdminPaymentFilter.RefundPrepared)).Items.Single().HasPreparedRefund);
        var detail = Assert.IsType<AdminPaymentDetails>(await query.FindAsync(payment.Id));
        Assert.False(detail.CanPrepareRefund);
        Assert.Equal(refund.Id, detail.RefundRequest!.Id);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("disabled", PaymentRefundPreparationStatus.Disabled)]
    [InlineData("invalid-config", PaymentRefundPreparationStatus.Disabled)]
    [InlineData("actor", PaymentRefundPreparationStatus.Forbidden)]
    [InlineData("revoked", PaymentRefundPreparationStatus.Forbidden)]
    [InlineData("missing", PaymentRefundPreparationStatus.NotFound)]
    [InlineData("payment-version", PaymentRefundPreparationStatus.Conflict)]
    [InlineData("order-version", PaymentRefundPreparationStatus.Conflict)]
    [InlineData("reason", PaymentRefundPreparationStatus.Invalid)]
    [InlineData("correlation", PaymentRefundPreparationStatus.Invalid)]
    [InlineData("clock", PaymentRefundPreparationStatus.NotEligible)]
    public async Task RejectsInvalidOrStaleRequestsWithoutIntention(string fault, PaymentRefundPreparationStatus expected)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var (payment, order) = await SeedAsync(db);
        var config = Configuration();
        var operation = Operation;
        var clock = new PaymentDispatchTests.Clock();
        if (fault == "disabled") { config.RefundPreparationEnabled = false; }
        if (fault == "invalid-config") { config.WebhookEnabled = false; }
        if (fault == "actor") { operation = operation with { ActorUserId = Guid.NewGuid() }; }
        if (fault == "reason") { operation = operation with { Reason = (PaymentRefundReason)999 }; }
        if (fault == "correlation") { operation = operation with { CorrelationId = "invalid\nvalue" }; }
        if (fault == "clock") { clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(-1); }
        if (fault == "revoked") { db.UserRoles.Remove(await db.UserRoles.SingleAsync()); await db.SaveChangesAsync(); }
        var result = await new PaymentRefundPreparationService(db, Options.Create(config), clock).PrepareAsync(
            fault == "missing" ? long.MaxValue : payment.Id,
            fault == "payment-version" ? Guid.NewGuid() : payment.ConcurrencyVersion,
            fault == "order-version" ? Guid.NewGuid() : order.ConcurrencyVersion, operation);
        Assert.Equal(expected, result.Status);
        Assert.Empty(await db.PaymentRefundRequests.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("attention")]
    [InlineData("production")]
    [InlineData("amount")]
    [InlineData("reference")]
    [InlineData("payment-id")]
    [InlineData("confirmation")]
    [InlineData("cancelled-order")]
    [InlineData("refunded-order")]
    public async Task IneligibleCaptureIsNeverPrepared(string fault)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var (payment, order) = await SeedAsync(db);
        // InMemory corruption fixtures exercise guards even against inconsistent historical data.
        db.Attach(payment);
        db.Attach(order);
        switch (fault)
        {
            case "pending": db.Entry(payment).Property(p => p.Status).CurrentValue = PaymentStatus.Pending; break;
            case "attention": db.Entry(payment).Property(p => p.Status).CurrentValue = PaymentStatus.RequiresAttention; break;
            case "production": db.Entry(payment).Property(p => p.Environment).CurrentValue = PaymentEnvironment.Production; break;
            case "amount": db.Entry(payment).Property(p => p.Amount).CurrentValue = 1m; break;
            case "reference": db.Entry(payment).Property(p => p.ExternalReference).CurrentValue = "wrong-order"; break;
            case "payment-id": db.Entry(payment).Property(p => p.ExternalPaymentId).CurrentValue = null; break;
            case "confirmation": db.Entry(payment).Property(p => p.ConfirmedAtUtc).CurrentValue = null; break;
            case "cancelled-order": db.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled; break;
            case "refunded-order": db.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Refunded; break;
        }
        await db.SaveChangesAsync();
        Assert.False(PaymentRefundRequest.CanPrepare(payment, order));
        Assert.Throws<InvalidOperationException>(() => new PaymentRefundRequest(Guid.NewGuid(), payment, order, Operation.ActorUserId, Operation.Reason, PaymentDispatchTests.Now));
        Assert.Equal(PaymentRefundPreparationStatus.NotEligible,
            (await Service(db).PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, Operation)).Status);
        Assert.Empty(await db.PaymentRefundRequests.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPreparationCreatesExactlyOneIntentionAndAudit()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var (payment, order) = await SeedAsync(db);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var isolated = app.Services.CreateScope();
            return await Service(isolated.ServiceProvider.GetRequiredService<SallvatDbContext>())
                .PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, Operation);
        }));
        Assert.Single(results, r => r.Status == PaymentRefundPreparationStatus.Prepared);
        Assert.Equal(7, results.Count(r => r.Status == PaymentRefundPreparationStatus.AlreadyPrepared));
        Assert.Single(results.Select(r => r.RequestId).Distinct());
        Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Single(await db.PaymentRefundRequests.ToListAsync());
    }

    [Fact]
    public async Task CancellationAndDirtyContextCannotPersistAnIntention()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var (payment, order) = await SeedAsync(db);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).PrepareAsync(payment.Id, payment.ConcurrencyVersion,
            order.ConcurrencyVersion, Operation, new CancellationToken(true)));
        db.Attach(order);
        db.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Preparing;
        Assert.Equal(PaymentRefundPreparationStatus.Conflict,
            (await Service(db).PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, Operation)).Status);
        Assert.Empty(await db.PaymentRefundRequests.ToListAsync());
    }

    internal static MercadoPagoOptions Configuration()
    {
        var options = PaymentWebhookTests.Configuration();
        options.RefundPreparationEnabled = true;
        return options;
    }

    internal static PaymentRefundPreparationService Service(SallvatDbContext db) =>
        new(db, Options.Create(Configuration()), new PaymentDispatchTests.Clock());

    internal static async Task<(Payment Payment, Order Order)> SeedAsync(SallvatDbContext db)
    {
        await PaymentWebhookTests.SeedAsync(db);
        Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new()).HandleAsync(PaymentWebhookTests.Request()));
        await PaymentRecoveryTests.SeedAdminAsync(db);
        db.ChangeTracker.Clear();
        return (await db.Payments.AsNoTracking().SingleAsync(), await db.Orders.AsNoTracking().SingleAsync());
    }
}
