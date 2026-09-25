using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentReconciliationTests
{
    [Fact]
    public async Task InspectsPersistedSnapshotWithoutChangingPaymentOrderOrStock()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var version = payment.ConcurrencyVersion;
        var order = await db.Orders.SingleAsync();
        var orderVersion = order.ConcurrencyVersion;
        var gateway = new Gateway();
        for (var i = 0; i < 2; i++)
        {
            var result = await Service(db, gateway).InspectAsync(payment.Id, PaymentDispatchTests.Owner);
            Assert.Equal(PaymentReconciliationStatus.AwaitingPayment, result.Status);
        }

        Assert.Equal(new PaymentOrderQuery("ORD-query", PaymentEnvironment.Sandbox, order.OrderNumber, 158.50m, "BRL"), gateway.Request);
        Assert.Equal(2, gateway.Calls);
        Assert.False(db.ChangeTracker.HasChanges());
        db.ChangeTracker.Clear();
        Assert.Equal(version, (await db.Payments.SingleAsync()).ConcurrencyVersion);
        Assert.Equal(orderVersion, (await db.Orders.SingleAsync()).ConcurrencyVersion);
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync()).Reserved);
    }

    [Theory]
    [InlineData(PaymentOrderQueryStatus.NotFound, PaymentReconciliationStatus.RequiresAttention, PaymentReconciliationReason.ProviderNotFound)]
    [InlineData(PaymentOrderQueryStatus.InvalidResponse, PaymentReconciliationStatus.RequiresAttention, PaymentReconciliationReason.ProviderMismatch)]
    [InlineData(PaymentOrderQueryStatus.Unavailable, PaymentReconciliationStatus.Unavailable, PaymentReconciliationReason.None)]
    [InlineData(PaymentOrderQueryStatus.AuthenticationFailure, PaymentReconciliationStatus.Unavailable, PaymentReconciliationReason.None)]
    public async Task FailedQueryDoesNotReleaseAttempt(PaymentOrderQueryStatus provider, PaymentReconciliationStatus expected, PaymentReconciliationReason reason)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var result = await Service(db, new Gateway { Result = new(provider) }).InspectAsync(payment.Id, PaymentDispatchTests.Owner);
        Assert.Equal(new PaymentReconciliationResult(expected, reason), result);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(ObservedOrderState.Processed, 158.5, true)]
    [InlineData(ObservedOrderState.Created, 1, false)]
    [InlineData(ObservedOrderState.Created, 0, true)]
    [InlineData(ObservedOrderState.ActionRequired, 0, false)]
    [InlineData(ObservedOrderState.Other, 0, false)]
    public async Task FinancialEvidenceOnlyRequestsReview(ObservedOrderState state, double paid, bool transactions)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var observation = Observation() with { State = state, PaidAmount = (decimal)paid, HasTransactions = transactions };
        var result = await Service(db, new Gateway { Result = new(PaymentOrderQueryStatus.Found, observation) })
            .InspectAsync((await db.Payments.SingleAsync()).Id, PaymentDispatchTests.Owner);
        Assert.Equal(PaymentReconciliationReason.FinancialActivityObserved, result.Reason);
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrAbandonedDispatchNeverSearchesBindsOrSendsAgain(bool finished)
    {
        await using var app = await CreateAsync(withId: false, finish: finished);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var version = payment.ConcurrencyVersion;
        var gateway = new Gateway();
        var result = await Service(db, gateway).InspectAsync(payment.Id, PaymentDispatchTests.Owner);
        Assert.Equal(PaymentReconciliationReason.MissingExternalOrderId, result.Reason);
        Assert.Equal(0, gateway.Calls);
        Assert.Equal(version, payment.ConcurrencyVersion);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task WrongOwnerNonexistentDisabledAndDirtyContextDoNotQuery()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var gateway = new Gateway();
        Assert.Equal(PaymentReconciliationStatus.NotFound, (await Service(db, gateway).InspectAsync(payment.Id, CartOwner.ForGuest(new string('B', 43)))).Status);
        Assert.Equal(PaymentReconciliationStatus.NotFound, (await Service(db, gateway).InspectAsync(-1, PaymentDispatchTests.Owner)).Status);
        var disabled = new PaymentReconciliationService(db, gateway, Options.Create(new MercadoPagoOptions()), new PaymentDispatchTests.Clock());
        Assert.Equal(PaymentReconciliationStatus.Disabled, (await disabled.InspectAsync(payment.Id, PaymentDispatchTests.Owner)).Status);
        (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, PaymentDispatchTests.Now);
        Assert.Equal(PaymentReconciliationStatus.Conflict, (await Service(db, gateway).InspectAsync(payment.Id, PaymentDispatchTests.Owner)).Status);
        Assert.True(db.ChangeTracker.HasChanges());
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task CancellationDuringHttpIsDetectedFromFreshState()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var gateway = new Gateway
        {
            DuringQuery = async () =>
            {
                using var other = app.Services.CreateScope();
                var write = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
                (await write.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, PaymentDispatchTests.Now);
                await write.SaveChangesAsync();
            },
        };
        var result = await Service(db, gateway).InspectAsync((await db.Payments.SingleAsync()).Id, PaymentDispatchTests.Owner);
        Assert.Equal(PaymentReconciliationStatus.Conflict, result.Status);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ExpiryOrLateDispatchCannotBeClearedByCreatedObservation()
    {
        foreach (var late in new[] { false, true })
        {
            await using var app = await CreateAsync(late: late);
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(30) };
            var result = await Service(db, new Gateway(), clock).InspectAsync((await db.Payments.SingleAsync()).Id, PaymentDispatchTests.Owner);
            Assert.Equal(PaymentReconciliationReason.LocalReviewRequired, result.Reason);
            Assert.False(db.ChangeTracker.HasChanges());
        }
    }

    private static PaymentReconciliationService Service(SallvatDbContext db, Gateway gateway, PaymentDispatchTests.Clock? clock = null) =>
        new(db, gateway, Options.Create(PaymentDispatchTests.Configuration()), clock ?? new PaymentDispatchTests.Clock());

    [Fact]
    public async Task GuestSessionExpiringDuringQueryDoesNotRevealResult()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock();
        var gateway = new Gateway
        {
            DuringQuery = () =>
            {
                clock.UtcNow = PaymentDispatchTests.Now.AddHours(3);
                return Task.CompletedTask;
            },
        };
        var result = await Service(db, gateway, clock).InspectAsync((await db.Payments.SingleAsync()).Id, PaymentDispatchTests.Owner);
        Assert.Equal(new PaymentReconciliationResult(PaymentReconciliationStatus.NotFound), result);
        Assert.Equal(1, gateway.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentIdOrObservationBeforeDispatchCannotMatchAttempt(bool oldObservation)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var observation = oldObservation ? Observation() with { CreatedAtUtc = PaymentDispatchTests.Now.AddMinutes(-6) }
            : Observation() with { ExternalOrderId = "ORD-another" };
        var result = await Service(db, new Gateway { Result = new(PaymentOrderQueryStatus.Found, observation) })
            .InspectAsync((await db.Payments.SingleAsync()).Id, PaymentDispatchTests.Owner);
        Assert.Equal(PaymentReconciliationReason.ProviderMismatch, result.Reason);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync(bool withId = true, bool finish = true, bool late = false)
    {
        var app = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentPreparationTests.SeedAsync(db);
        await new PaymentPreparationService(db, new PaymentDispatchTests.Clock()).PrepareAsync(1_000, PaymentDispatchTests.Owner, PaymentEnvironment.Sandbox);
        var payment = await db.Payments.SingleAsync();
        var token = Guid.NewGuid();
        payment.TryBeginOrderDispatch(token, PaymentDispatchTests.Now);
        if (finish)
        {
            payment.CompleteOrderDispatch(token, withId ? "ORD-query" : null, !late, PaymentDispatchTests.Now);
        }

        await db.SaveChangesAsync();
        return app;
    }

    private static PaymentOrderObservation Observation() => new("ORD-query", ObservedOrderState.Created, 0,
        PaymentDispatchTests.Now, PaymentDispatchTests.Now, false);

    private sealed class Gateway : IPaymentGateway
    {
        public int Calls { get; private set; }
        public PaymentOrderQuery? Request { get; private set; }
        public PaymentOrderQueryResult Result { get; init; } = new(PaymentOrderQueryStatus.Found, Observation());
        public Func<Task>? DuringQuery { get; init; }
        public async Task<PaymentOrderQueryResult> GetOrderAsync(PaymentOrderQuery request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            if (DuringQuery is not null)
            {
                await DuringQuery();
            }

            return Result;
        }

        public Task<PaymentOrderResult> CreateOrderAsync(PaymentOrderRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reconciliation must never create an order.");
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(PaymentPreferenceRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reconciliation must never create a preference.");
    }
}
