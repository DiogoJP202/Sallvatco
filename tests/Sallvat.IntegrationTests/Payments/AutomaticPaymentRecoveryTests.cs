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

public sealed class AutomaticPaymentRecoveryTests
{
    internal static MercadoPagoOptions Configuration()
    {
        var options = PaymentRecoveryTests.Configuration();
        options.AutomaticRecoveryEnabled = true;
        return options;
    }

    internal static PaymentRecoveryBatchService Service(SallvatDbContext db, PaymentWebhookTests.Gateway gateway, PaymentDispatchTests.Clock clock) =>
        new(db, gateway, Options.Create(Configuration()), clock);

    [Fact]
    public async Task DisabledWorkerDoesNotNeedDatabaseAndInvalidOptionsCannotEnableIt()
    {
        await using var app = new AccountWebApplicationFactory();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var gateway = new PaymentWebhookTests.Gateway();
        Assert.Equal(0, await new PaymentRecoveryBatchService(db, gateway, Options.Create(new MercadoPagoOptions()), new PaymentDispatchTests.Clock()).RunAsync(20));
        Assert.Equal(0, gateway.Calls);
        Assert.False(new MercadoPagoOptions().AutomaticRecoveryEnabled);
        var options = Configuration();
        Assert.True(MercadoPagoOptions.IsValid(options));
        options.RecoveryEnabled = false;
        Assert.False(MercadoPagoOptions.IsValid(options));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service(db, gateway, new()).RunAsync(21));
    }

    [Fact]
    public async Task BackoffSurvivesNewServiceInstancesAndBudgetDoesNotBlockManualRecovery()
    {
        await using var app = await CreateAsync();
        var clock = new PaymentDispatchTests.Clock();
        var gateway = new PaymentWebhookTests.Gateway { Result = new(PaymentOrderQueryStatus.Unavailable) };
        foreach (var (minute, count) in new[] { (0d, 0), (1.99, 0), (2d, 1), (6.99, 1), (7d, 2), (21.99, 2), (22d, 3), (60d, 3) })
        {
            clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(minute);
            using var iteration = app.Services.CreateScope();
            await Service(iteration.ServiceProvider.GetRequiredService<SallvatDbContext>(), gateway, clock).RunAsync(20);
            Assert.Equal(count, gateway.Calls);
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(3, await db.PaymentRecoveryExecutions.CountAsync());
        Assert.All(await db.PaymentRecoveryExecutions.ToListAsync(), e =>
        {
            Assert.Equal(PaymentRecoverySource.Automatic, e.Source);
            Assert.Equal(PaymentRecoveryOutcome.Unavailable, e.Outcome);
        });
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
        // A manual action stays available, without resetting the automatic budget.
        await PaymentRecoveryTests.SeedAdminAsync(db);
        var payment = await db.Payments.SingleAsync();
        clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(60);
        Assert.Equal(PaymentRecoveryResult.RequiresAttention, await PaymentRecoveryTests.Service(db, new(), clock)
            .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
        Assert.Equal(3, await db.PaymentRecoveryExecutions.CountAsync(e => e.Source == PaymentRecoverySource.Automatic));
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task AutomaticConfirmationHasDurableSystemEvidenceWithoutInventingAdminOrWebhook()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        var gateway = new PaymentWebhookTests.Gateway();
        Assert.Equal(1, await Service(db, gateway, clock).RunAsync(20));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        var execution = await db.PaymentRecoveryExecutions.SingleAsync();
        Assert.Equal(PaymentRecoverySource.Automatic, execution.Source);
        Assert.Equal(PaymentRecoveryOutcome.Confirmed, execution.Outcome);
        Assert.Equal(PaymentRecoveryExecutionState.Completed, execution.State);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Equal(0, await Service(db, gateway, clock).RunAsync(20));
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task RunningAutomaticExecutionBlocksOtherWorkerAndManualQuery()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentRecoveryTests.SeedAdminAsync(db);
        var payment = await db.Payments.SingleAsync();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
        {
            using var other = app.Services.CreateScope();
            var otherDb = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            var otherGateway = new PaymentWebhookTests.Gateway();
            Assert.Equal(0, await Service(otherDb, otherGateway, clock).RunAsync(20));
            Assert.Equal(PaymentRecoveryResult.Busy, await PaymentRecoveryTests.Service(otherDb, otherGateway, clock)
                .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
            Assert.Equal(0, otherGateway.Calls);
        }
        };
        await Service(db, gateway, clock).RunAsync(20);
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Single(await db.PaymentRecoveryExecutions.ToListAsync());
    }

    [Fact]
    public async Task CancelledGetConsumesBudgetAndRestartWaitsForBackoff()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        using var cancellation = new CancellationTokenSource();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = () =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db, gateway, clock).RunAsync(20, cancellation.Token));
        Assert.Equal(PaymentRecoveryExecutionState.Running, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
        clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(4);
        var retry = new PaymentWebhookTests.Gateway();
        Assert.Equal(0, await Service(db, retry, clock).RunAsync(20));
        clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(7);
        Assert.Equal(1, await Service(db, retry, clock).RunAsync(20));
        Assert.Equal(1, retry.Calls);
        Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.Outcome == PaymentRecoveryOutcome.Interrupted));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualReplacementOrWebhookWinsWithoutLateAutomaticSale(bool webhook)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentRecoveryTests.SeedAdminAsync(db);
        var payment = await db.Payments.SingleAsync();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
        {
            clock.UtcNow += PaymentRecoveryExecution.Lifetime;
            using var other = app.Services.CreateScope();
            var otherDb = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            if (webhook)
            {
                Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(otherDb, new(), clock)
                    .HandleAsync(PaymentWebhookTests.Request(now: clock.UtcNow)));
            }
            else
            {
                Assert.Equal(PaymentRecoveryResult.Observed, await PaymentRecoveryTests.Service(otherDb,
                    new()
                    {
                        Result = new(PaymentOrderQueryStatus.Found, PaymentWebhookTests.Observation() with
                        { State = ObservedOrderState.Created, PaidAmount = 0, HasTransactions = false, SettledPaymentId = null })
                    }, clock)
                    .RecoverAsync(payment.Id, payment.ConcurrencyVersion, PaymentRecoveryTests.Operation));
            }
        }
        };
        await Service(db, gateway, clock).RunAsync(20);
        Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.Source == PaymentRecoverySource.Automatic && e.Outcome == PaymentRecoveryOutcome.Interrupted));
        if (webhook) { await PaymentWebhookTests.AssertConfirmedAsync(db); }
        else { Assert.Empty(await db.InventoryMovements.ToListAsync()); }
    }

    [Theory]
    [InlineData("old")]
    [InlineData("review")]
    [InlineData("missing")]
    public async Task UnsafeOrOldAttemptsAreNotSelected(string kind)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        if (kind == "old") { clock.UtcNow = PaymentDispatchTests.Now.AddHours(24).AddTicks(1); }
        else if (kind == "review") { payment.RequireCanonicalReview(PaymentAttentionReason.FinancialReview, clock.UtcNow); }
        else { db.Entry(payment).Property(p => p.ExternalOrderId).CurrentValue = null; }
        await db.SaveChangesAsync();
        var gateway = new PaymentWebhookTests.Gateway();
        Assert.Equal(0, await Service(db, gateway, clock).RunAsync(20));
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.PaymentRecoveryExecutions.ToListAsync());
    }

    [Fact]
    public async Task LateCaptureIsReviewedWithoutReopeningCancelledOrder()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, clock.UtcNow);
        await db.SaveChangesAsync();
        await Service(db, new(), clock).RunAsync(20);
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(PaymentRecoveryOutcome.RequiresAttention, (await db.PaymentRecoveryExecutions.SingleAsync()).Outcome);
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task BatchLimitAppliesAfterFilteringAlreadyUsedBudgets()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var first = await db.Payments.SingleAsync();
        var original = await db.Orders.SingleAsync();
        var now = PaymentDispatchTests.Now;
        for (var index = 0; index < 3; index++)
        {
            var used = new PaymentRecoveryExecution(Guid.NewGuid(), first.Id, now, PaymentRecoverySource.Automatic);
            used.Complete(now, PaymentRecoveryOutcome.Unavailable);
            db.PaymentRecoveryExecutions.Add(used);
            var order = new Order(2_000 + index, $"SVT-20260921-{2000 + index:D8}", Guid.NewGuid(), original.SourceCartId, null,
                "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
                "Melhor Envio", "Transportadora", "Expresso", $"quote-{index}", 2, 4, now, now, now.AddMinutes(30));
            var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), now);
            var token = Guid.NewGuid();
            payment.TryBeginOrderDispatch(token, now);
            payment.CompleteOrderDispatch(token, $"ORD-batch-{index}", true, now);
            db.Orders.Add(order);
            db.Payments.Add(payment);
        }
        await db.SaveChangesAsync();
        var clock = new PaymentDispatchTests.Clock { UtcNow = now.AddMinutes(2) };
        var gateway = new PaymentWebhookTests.Gateway { Result = new(PaymentOrderQueryStatus.Unavailable) };
        Assert.Equal(2, await Service(db, gateway, clock).RunAsync(2));
        Assert.Equal(2, gateway.Calls);
        Assert.Equal(1, await Service(db, gateway, clock).RunAsync(2));
        Assert.Equal(3, gateway.Calls);
        Assert.Equal(0, await Service(db, gateway, clock).RunAsync(2));
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
