using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentDispatchTests
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
    internal static readonly CartOwner Owner = CartOwner.ForGuest(new string('A', 43));

    [Fact]
    public async Task ConcurrentDispatchAndReplayOnlySendOnceAndKeepStockUnchanged()
    {
        await using var app = await CreateAsync();
        var gateway = new Gateway();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => DispatchAsync(app, gateway)));
        Assert.Contains(results, r => r.Status == PaymentDispatchStatus.Ready);
        Assert.All(results, r => Assert.Contains(r.Status, new[] { PaymentDispatchStatus.Ready, PaymentDispatchStatus.Conflict, PaymentDispatchStatus.RequiresAttention }));
        Assert.Equal(PaymentDispatchStatus.Ready, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(1, gateway.Calls);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        Assert.Equal("ORD-dispatch", payment.ExternalOrderId);
        Assert.Equal(PaymentDispatchState.Completed, payment.DispatchState);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
        var variant = await db.ProductVariants.SingleAsync();
        Assert.Equal(4, variant.OnHand);
        Assert.Equal(2, variant.Reserved);
        Assert.Equal(payment.IdempotencyKey, gateway.Request!.IdempotencyKey);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(PaymentOrderStatus.Rejected)]
    [InlineData(PaymentOrderStatus.AuthenticationFailure)]
    [InlineData(PaymentOrderStatus.Disabled)]
    public async Task FailedResultsAreDurablyBlockedWithoutNewPost(PaymentOrderStatus result)
    {
        await using var app = await CreateAsync();
        var gateway = new Gateway { Respond = (_, _) => Task.FromResult(new PaymentOrderResult(result)) };
        Assert.Equal(PaymentDispatchStatus.RequiresAttention, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(PaymentDispatchStatus.RequiresAttention, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(1, gateway.Calls);
        using var scope = app.Services.CreateScope();
        var payment = await scope.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.SingleAsync();
        Assert.Equal(PaymentDispatchState.RequiresAttention, payment.DispatchState);
        Assert.Equal(PaymentAttentionReason.OrderOutcomeUnknown, payment.AttentionReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrExpiryDuringHttpRetainsExternalIdWithoutReturningCheckout(bool expire)
    {
        await using var app = await CreateAsync();
        var clock = new Clock();
        var gateway = new Gateway
        {
            Respond = async (_, _) =>
            {
                if (expire)
                {
                    clock.UtcNow = Now.AddMinutes(30);
                }
                else
                {
                    using var scope = app.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
                    (await db.Orders.SingleAsync(CancellationToken.None)).TransitionTo(OrderStatus.Cancelled, Now);
                    await db.SaveChangesAsync(CancellationToken.None);
                }

                return Gateway.Success();
            },
        };
        var result = await DispatchAsync(app, gateway, clock: clock);
        Assert.Equal(PaymentDispatchStatus.RequiresAttention, result.Status);
        Assert.Null(result.CheckoutUrl);
        using var read = app.Services.CreateScope();
        var payment = await read.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.SingleAsync();
        Assert.Equal("ORD-dispatch", payment.ExternalOrderId);
        Assert.Equal(PaymentAttentionReason.LateOrderResponse, payment.AttentionReason);
    }

    [Fact]
    public async Task BrowserCancellationAfterClaimDoesNotCancelResultPersistence()
    {
        await using var app = await CreateAsync();
        using var browser = new CancellationTokenSource();
        var gateway = new Gateway
        {
            Respond = (_, token) =>
            {
                browser.Cancel();
                Assert.False(token.IsCancellationRequested);
                return Task.FromResult(Gateway.Success());
            },
        };
        Assert.Equal(PaymentDispatchStatus.Ready, (await DispatchAsync(app, gateway, cancellationToken: browser.Token)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchAsync(app, gateway, cancellationToken: browser.Token));
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task AbandonedClaimIsNeverAutomaticallyReclaimed()
    {
        await using var app = await CreateAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            (await db.Payments.SingleAsync()).TryBeginOrderDispatch(Guid.NewGuid(), Now);
            await db.SaveChangesAsync();
        }

        var gateway = new Gateway();
        Assert.Equal(PaymentDispatchStatus.RequiresAttention, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task WrongOwnerDisabledAndCancelledOrdersDoNotSend()
    {
        await using var app = await CreateAsync();
        var gateway = new Gateway();
        Assert.Equal(PaymentDispatchStatus.NotFound, (await DispatchAsync(app, gateway, owner: CartOwner.ForGuest(new string('B', 43)))).Status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var payment = await db.Payments.SingleAsync();
        var disabled = new PaymentDispatchService(db, gateway, Options.Create(new MercadoPagoOptions()), new Clock());
        Assert.Equal(PaymentDispatchStatus.Disabled, (await disabled.DispatchAsync(payment.Id, Owner)).Status);
        (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, Now);
        await db.SaveChangesAsync();
        Assert.Equal(PaymentDispatchStatus.Invalid, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task SavedSnapshotsAreSplitIntoExactCentAmounts()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var original = await db.Orders.SingleAsync();
        var order = new Order(2_000, "SVT-20260921-00002000", Guid.NewGuid(), original.SourceCartId, original.CustomerId,
            "Cliente", "cliente@example.com", "11999998888", 140m, .01m, 18.50m, "BRL", 1, "CENTAVO",
            "Melhor Envio", "Transportadora", "Expresso", "quote", 2, 4, Now, Now, Now.AddMinutes(30));
        var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), Now);
        var item = new OrderItem(order.Id, 1, "Perfume", "50 ml", "SKU", 2, 70m, .01m);
        var request = PaymentDispatchService.BuildRequest(payment, order, [item]);
        Assert.NotNull(request);
        Assert.Equal(158.49m, request.Items.Sum(i => i.UnitAmount * i.Quantity));
        Assert.Equal([69.99m, 70m, 18.50m], request.Items.Select(i => i.UnitAmount));
        Assert.Equal(3, request.Items.Select(i => i.Id).Distinct().Count());
    }

    internal static MercadoPagoOptions Configuration() => new()
    {
        OrdersEnabled = true,
        TestSellerConfirmed = true,
        TestSellerId = 123,
        AccessToken = "test-only",
        PublicOrigin = "https://staging.example.com",
    };

    [Fact]
    public async Task FailureSavingResponseLeavesClaimAndNeverRepeatsPost()
    {
        await using var app = await CreateAsync();
        var gateway = new Gateway();
        using (var scope = app.Services.CreateScope())
        {
            var options = new DbContextOptionsBuilder<SallvatDbContext>(scope.ServiceProvider.GetRequiredService<DbContextOptions<SallvatDbContext>>())
                .AddInterceptors(new FailCompletionSave()).Options;
            await using var db = new SallvatDbContext(options);
            var id = await db.Payments.Select(p => p.Id).SingleAsync();
            var result = await new PaymentDispatchService(db, gateway, Options.Create(Configuration()), new Clock()).DispatchAsync(id, Owner);
            Assert.Equal(PaymentDispatchStatus.RequiresAttention, result.Status);
        }

        Assert.Equal(PaymentDispatchStatus.RequiresAttention, (await DispatchAsync(app, gateway)).Status);
        Assert.Equal(1, gateway.Calls);
        using var read = app.Services.CreateScope();
        var payment = await read.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.SingleAsync();
        Assert.Equal(PaymentDispatchState.Sending, payment.DispatchState);
        Assert.Null(payment.ExternalOrderId);
    }

    private sealed class FailCompletionSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Payment>().Any(e => e.Entity.DispatchState == PaymentDispatchState.Completed))
            {
                throw new DbUpdateException("Simulated database failure after HTTP.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync()
    {
        var app = new AccountWebApplicationFactory(clock: new Clock());
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await PaymentPreparationTests.SeedAsync(db);
        await new PaymentPreparationService(db, new Clock()).PrepareAsync(1_000, Owner, PaymentEnvironment.Sandbox);
        return app;
    }

    private static async Task<PaymentDispatchResult> DispatchAsync(AccountWebApplicationFactory app, Gateway gateway,
        CartOwner? owner = null, Clock? clock = null, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var id = await db.Payments.Select(p => p.Id).SingleAsync(CancellationToken.None);
        return await new PaymentDispatchService(db, gateway, Options.Create(Configuration()), clock ?? new Clock())
            .DispatchAsync(id, owner ?? Owner, cancellationToken);
    }

    internal sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    internal sealed class Gateway : IPaymentGateway
    {
        private int calls;
        public int Calls => calls;
        public PaymentOrderRequest? Request { get; private set; }
        public Func<PaymentOrderRequest, CancellationToken, Task<PaymentOrderResult>>? Respond { get; init; }
        public async Task<PaymentOrderResult> CreateOrderAsync(PaymentOrderRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            Request = request;
            return Respond is null ? Success() : await Respond(request, cancellationToken);
        }

        public Task<PaymentPreferenceResult> CreatePreferenceAsync(PaymentPreferenceRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Preferences must never be called by Orders dispatch.");

        internal static PaymentOrderResult Success() => new(PaymentOrderStatus.Created, "ORD-dispatch",
            new Uri("https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORD-dispatch"));
    }
}
