using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentRecoveryTests
{
    internal static readonly Guid AdminId = Guid.Parse("60e95475-0e53-4878-aebd-6a1b8101b574");
    internal static PaymentRecoveryOperation Operation => new(AdminId, PaymentRecoveryReason.MissingNotification, "recovery-test");

    [Fact]
    public async Task TestAdminReusesRoleAlreadyCreatedByMigrations()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var role = await db.Roles.SingleAsync(r => r.NormalizedName == "ADMIN");
        await SeedAdminAsync(db);
        Assert.Equal(role.Id, (await db.UserRoles.SingleAsync()).RoleId);
        Assert.Single(await db.Roles.Where(r => r.NormalizedName == "ADMIN").ToListAsync());
    }

    [Fact]
    public async Task RecoversLostNotificationWithIntentAndAtomicAuditWithoutInventingWebhookReceipt()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var version = payment.ConcurrencyVersion;
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () => Assert.Equal("payment.recovery.requested", (await db.AuditLogs.SingleAsync()).Action),
        };
        Assert.Equal(PaymentRecoveryResult.Confirmed, await Service(db, gateway).RecoverAsync(payment.Id, version, Operation));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        var audits = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal(PaymentRecoveryExecutionState.Completed, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
        Assert.Equal("payment.recovery.completed", audits[1].Action);
        using var intent = JsonDocument.Parse(audits[0].ChangesJson);
        using var completed = JsonDocument.Parse(audits[1].ChangesJson);
        Assert.Equal(intent.RootElement.GetProperty("RequestId").GetGuid(), completed.RootElement.GetProperty("RequestId").GetGuid());
        Assert.Equal("Confirmed", completed.RootElement.GetProperty("Result").GetString());
        Assert.All(audits, a =>
        {
            Assert.Equal(AdminId, a.ActorUserId);
            Assert.DoesNotContain("cliente@example.com", a.ChangesJson);
            Assert.DoesNotContain(payment.IdempotencyKey.ToString(), a.ChangesJson);
            Assert.DoesNotContain("ORD-webhook", a.ChangesJson);
        });
        Assert.Equal(PaymentRecoveryResult.Conflict, await Service(db, gateway).RecoverAsync(payment.Id, version, Operation));
        Assert.Equal(PaymentRecoveryResult.NotEligible, await Service(db, gateway).RecoverAsync(payment.Id, (await db.Payments.SingleAsync()).ConcurrencyVersion, Operation));
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new()).HandleAsync(PaymentWebhookTests.Request()));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Equal(WebhookOutcome.Observed, (await db.WebhookEvents.SingleAsync()).Outcome);
    }

    [Theory]
    [InlineData(PaymentOrderQueryStatus.Unavailable)]
    [InlineData(PaymentOrderQueryStatus.NotFound)]
    [InlineData(PaymentOrderQueryStatus.AuthenticationFailure)]
    public async Task FailedGetIsAuditedAndNeverDispatchesAgain(PaymentOrderQueryStatus status)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var gateway = new PaymentWebhookTests.Gateway { Result = new(status) };
        Assert.Equal(PaymentRecoveryResult.Unavailable, await Service(db, gateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Equal(PaymentRecoveryExecutionState.Completed, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Equal(PaymentRecoveryResult.Confirmed, await Service(db, new()).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCaptureRequiresReviewWithoutReopeningOrder(bool cancelled)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        if (cancelled)
        {
            (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, PaymentDispatchTests.Now);
            await db.SaveChangesAsync();
        }

        var payment = await db.Payments.SingleAsync();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddHours(1) };
        Assert.Equal(PaymentRecoveryResult.RequiresAttention, await Service(db, new(), clock).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(cancelled ? OrderStatus.Cancelled : OrderStatus.RequiresAttention, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRequestCannotStartAnotherGetWhileExecutionIsRunning()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var otherGateway = new PaymentWebhookTests.Gateway();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
            {
                using var other = app.Services.CreateScope();
                Assert.Equal(PaymentRecoveryResult.Busy, await Service(other.ServiceProvider.GetRequiredService<SallvatDbContext>(), otherGateway)
                    .RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
            },
        };
        Assert.Equal(PaymentRecoveryResult.Confirmed, await Service(db, gateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(0, otherGateway.Calls);
        Assert.Single(await db.PaymentRecoveryExecutions.ToListAsync());
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredResponseNeverAppliesEvenWhenAReplacementAlreadyCompleted(bool replace)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var clock = new PaymentDispatchTests.Clock();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
            {
                clock.UtcNow += PaymentRecoveryExecution.Lifetime;
                if (replace)
                {
                    using var other = app.Services.CreateScope();
                    var pending = new PaymentWebhookTests.Gateway
                    {
                        Result = new(PaymentOrderQueryStatus.Found,
                        PaymentWebhookTests.Observation() with { State = ObservedOrderState.Created, PaidAmount = 0, HasTransactions = false, SettledPaymentId = null })
                    };
                    Assert.Equal(PaymentRecoveryResult.Observed, await Service(other.ServiceProvider.GetRequiredService<SallvatDbContext>(), pending, clock)
                        .RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
                }
            },
        };
        Assert.Equal(PaymentRecoveryResult.Interrupted, await Service(db, gateway, clock).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.State == PaymentRecoveryExecutionState.Interrupted));
        Assert.Equal(replace ? 1 : 0, await db.PaymentRecoveryExecutions.CountAsync(e => e.State == PaymentRecoveryExecutionState.Completed));
        Assert.Equal(PaymentRecoveryResult.Confirmed, await Service(db, new(), clock).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedOrderOrRevokedAdminDuringGetCannotConfirm(bool revoke)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
            {
                using var other = app.Services.CreateScope();
                var otherDb = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
                if (revoke)
                {
                    otherDb.UserRoles.Remove(await otherDb.UserRoles.SingleAsync());
                }
                else
                {
                    (await otherDb.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, PaymentDispatchTests.Now);
                }

                await otherDb.SaveChangesAsync();
            },
        };
        Assert.Equal(revoke ? PaymentRecoveryResult.Forbidden : PaymentRecoveryResult.Conflict,
            await Service(db, gateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task WebhookWinningDuringGetMakesRecoveryConflictWithoutSecondSale()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = async () =>
            {
                using var other = app.Services.CreateScope();
                Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(
                    other.ServiceProvider.GetRequiredService<SallvatDbContext>(), new()).HandleAsync(PaymentWebhookTests.Request()));
            },
        };
        Assert.Equal(PaymentRecoveryResult.Conflict, await Service(db, gateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Single(await db.WebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task GuardsRejectDisabledUnauthorizedStaleMissingAndDirtyRequestsWithoutGet()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var gateway = new PaymentWebhookTests.Gateway();
        var disabled = new PaymentRecoveryService(db, gateway, Options.Create(PaymentDispatchTests.Configuration()), new PaymentDispatchTests.Clock());
        Assert.Equal(PaymentRecoveryResult.Disabled, await disabled.RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        var service = Service(db, gateway);
        Assert.Equal(PaymentRecoveryResult.Forbidden, await service.RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation with { ActorUserId = Guid.NewGuid() }));
        Assert.Equal(PaymentRecoveryResult.Invalid, await service.RecoverAsync(payment.Id, Guid.Empty, Operation));
        Assert.Equal(PaymentRecoveryResult.Invalid, await service.RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation with { Reason = (PaymentRecoveryReason)99 }));
        Assert.Equal(PaymentRecoveryResult.NotFound, await service.RecoverAsync(99999, Guid.NewGuid(), Operation));
        Assert.Equal(PaymentRecoveryResult.Conflict, await service.RecoverAsync(payment.Id, Guid.NewGuid(), Operation));
        (await db.Carts.SingleAsync()).Refresh(PaymentDispatchTests.Now.AddMinutes(1), PaymentDispatchTests.Now.AddHours(3));
        Assert.Equal(PaymentRecoveryResult.Conflict, await service.RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.True(db.ChangeTracker.HasChanges());
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingIdAndReviewNeverSearchOrBindOrClearAttention(bool missingId)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentPreparationTests.SeedAsync(db);
        await SeedAdminAsync(db);
        var payment = new Payment(await db.Orders.SingleAsync(), PaymentEnvironment.Sandbox, Guid.NewGuid(), PaymentDispatchTests.Now);
        var token = Guid.NewGuid();
        payment.TryBeginOrderDispatch(token, PaymentDispatchTests.Now);
        payment.CompleteOrderDispatch(token, missingId ? null : "ORD-webhook", !missingId, PaymentDispatchTests.Now);
        if (!missingId)
        {
            payment.RequireCanonicalReview(PaymentAttentionReason.FinancialReview, PaymentDispatchTests.Now);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        var gateway = new PaymentWebhookTests.Gateway();
        Assert.Equal(PaymentRecoveryResult.NotEligible, await Service(db, gateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task CancellationAfterIntentLeavesNoFinancialEffectsOrFalseCompletion()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        using var cancellation = new CancellationTokenSource();
        var gateway = new PaymentWebhookTests.Gateway
        {
            DuringQuery = () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db, gateway)
            .RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation, cancellation.Token));
        Assert.Equal("payment.recovery.requested", (await db.AuditLogs.SingleAsync()).Action);
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Equal(PaymentRecoveryExecutionState.Running, (await db.PaymentRecoveryExecutions.SingleAsync()).State);
        var retryGateway = new PaymentWebhookTests.Gateway();
        Assert.Equal(PaymentRecoveryResult.Busy, await Service(db, retryGateway).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(0, retryGateway.Calls);
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(2) };
        Assert.Equal(PaymentRecoveryResult.Confirmed, await Service(db, retryGateway, clock).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.State == PaymentRecoveryExecutionState.Interrupted));
        Assert.Equal(1, await db.PaymentRecoveryExecutions.CountAsync(e => e.State == PaymentRecoveryExecutionState.Completed));
        Assert.Equal(4, await db.AuditLogs.CountAsync());
        await PaymentWebhookTests.AssertConfirmedAsync(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatedObservationDoesNotApproveAndMalformedResponseRequiresReview(bool malformed)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var result = malformed ? new PaymentOrderQueryResult(PaymentOrderQueryStatus.InvalidResponse)
            : new(PaymentOrderQueryStatus.Found, PaymentWebhookTests.Observation() with { State = ObservedOrderState.Created, PaidAmount = 0, HasTransactions = false, SettledPaymentId = null });
        Assert.Equal(malformed ? PaymentRecoveryResult.RequiresAttention : PaymentRecoveryResult.Observed,
            await Service(db, new() { Result = result }).RecoverAsync(payment.Id, payment.ConcurrencyVersion, Operation));
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public void RecoveryRequiresOrdersAndRemainsDisabledByDefault()
    {
        Assert.False(new MercadoPagoOptions().RecoveryEnabled);
        var options = Configuration();
        Assert.True(MercadoPagoOptions.IsValid(options));
        options.OrdersEnabled = false;
        Assert.False(MercadoPagoOptions.IsValid(options));
    }

    internal static PaymentRecoveryService Service(SallvatDbContext db, PaymentWebhookTests.Gateway gateway, PaymentDispatchTests.Clock? clock = null) =>
        new(db, gateway, Options.Create(Configuration()), clock ?? new PaymentDispatchTests.Clock());

    internal static MercadoPagoOptions Configuration()
    {
        var options = PaymentDispatchTests.Configuration();
        options.RecoveryEnabled = true;
        return options;
    }

    internal static async Task SeedAdminAsync(SallvatDbContext db)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == "ADMIN");
        if (role is null)
        {
            role = new IdentityRole<Guid>("Admin") { Id = Guid.NewGuid(), NormalizedName = "ADMIN" };
            db.Roles.Add(role);
        }

        db.Users.Add(new ApplicationUser
        {
            Id = AdminId,
            UserName = "recovery-test@example.invalid",
            NormalizedUserName = "RECOVERY-TEST@EXAMPLE.INVALID",
            Email = "recovery-test@example.invalid",
            NormalizedEmail = "RECOVERY-TEST@EXAMPLE.INVALID"
        });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = AdminId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync()
    {
        var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentWebhookTests.SeedAsync(db);
        await SeedAdminAsync(db);
        return app;
    }
}
