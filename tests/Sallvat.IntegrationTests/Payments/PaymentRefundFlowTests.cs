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

public sealed class PaymentRefundFlowTests
{
    internal static PaymentRefundActor Actor => new(PaymentRecoveryTests.AdminId, "refund-flow");

    [Fact]
    public async Task SubmissionIsExclusiveThenCanonicalCheckRefundsWithoutRestocking()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        var gateway = new Gateway
        {
            DuringPost = async () =>
            {
                using var other = app.Services.CreateScope();
                var check = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
                Assert.Equal(PaymentRefundRequestState.Sending, (await check.PaymentRefundRequests.SingleAsync()).State);
                Assert.Contains(await check.AuditLogs.ToListAsync(), a => a.Action == "payment.refund.dispatch.started");
            },
        };
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(db, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        Assert.Equal(request.Id, gateway.Submitted!.IdempotencyKey);
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(db, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        Assert.Equal(1, gateway.Posts);
        request = await db.PaymentRefundRequests.AsNoTracking().SingleAsync();
        gateway.Result = Refunded();
        Assert.Equal(PaymentRefundResult.Confirmed, await Service(db, gateway).CheckAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        Assert.Equal(PaymentRefundResult.Confirmed, await Service(db, gateway).CheckAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        await AssertRefundedAsync(db);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == "payment.refund.checked").ToListAsync());
        var receiptsBefore = await db.WebhookEvents.CountAsync();
        Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(db, new() { Result = Refunded() })
            .HandleAsync(PaymentWebhookTests.Request("refund-duplicate")));
        await AssertRefundedAsync(db);
        Assert.Equal(receiptsBefore + 1, await db.WebhookEvents.CountAsync());
        Assert.Equal(1, gateway.Posts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownSubmissionOrBrowserDisconnectNeverPermitsAnotherPost(bool disconnect)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        using var browser = new CancellationTokenSource();
        var gateway = new Gateway
        {
            DuringPost = () => { if (disconnect) { browser.Cancel(); } return Task.CompletedTask; },
            ThrowOnPost = !disconnect,
        };
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(db, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor, browser.Token));
        Assert.False(gateway.PostTokenCancelled);
        await PaymentWebhookTests.AssertConfirmedAsync(db);
        using var restart = app.Services.CreateScope();
        var restartedDb = restart.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(restartedDb, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        var current = await restartedDb.PaymentRefundRequests.AsNoTracking().SingleAsync();
        gateway.Result = Refunded();
        Assert.Equal(PaymentRefundResult.Confirmed, await Service(restartedDb, gateway).CheckAsync(request.PaymentId, current.ConcurrencyVersion, Actor));
        await AssertRefundedAsync(restartedDb);
        Assert.Equal(1, gateway.Posts);
    }

    [Fact]
    public async Task SignedWebhookCanConfirmDuringPostWithoutBeingOverwrittenByItsResponse()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        var gateway = new Gateway
        {
            DuringPost = async () =>
        {
            using var other = app.Services.CreateScope();
            var check = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            Assert.Equal(PaymentWebhookResult.Accepted, await PaymentWebhookTests.Service(check, new() { Result = Refunded() })
                .HandleAsync(PaymentWebhookTests.Request("refund-during-post")));
        }
        };
        Assert.Equal(PaymentRefundResult.Confirmed, await Service(db, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        await AssertRefundedAsync(db);
        Assert.Contains(await db.WebhookEvents.ToListAsync(), e => e.Outcome == WebhookOutcome.Refunded);
    }

    [Theory]
    [InlineData("disabled", PaymentRefundResult.Disabled)]
    [InlineData("stale", PaymentRefundResult.Conflict)]
    [InlineData("actor", PaymentRefundResult.Forbidden)]
    [InlineData("revoked-during-get", PaymentRefundResult.Forbidden)]
    [InlineData("order-changed", PaymentRefundResult.Conflict)]
    [InlineData("external-refund", PaymentRefundResult.RequiresAttention)]
    [InlineData("mismatch", PaymentRefundResult.RequiresAttention)]
    [InlineData("unavailable", PaymentRefundResult.Unavailable)]
    public async Task PreflightAndClaimRejectUnsafeSubmissions(string fault, PaymentRefundResult expected)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        var gateway = new Gateway();
        if (fault == "external-refund") { gateway.Result = Refunded(); }
        if (fault == "mismatch") { gateway.Result = new(PaymentOrderQueryStatus.Found, PaymentWebhookTests.Observation() with { SettledPaymentId = "PAY-other" }); }
        if (fault == "unavailable") { gateway.Result = new(PaymentOrderQueryStatus.Unavailable); }
        gateway.DuringGet = async () =>
        {
            using var other = app.Services.CreateScope();
            var check = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            if (fault == "revoked-during-get") { check.UserRoles.Remove(await check.UserRoles.SingleAsync()); }
            if (fault == "order-changed") { (await check.Orders.SingleAsync()).TransitionTo(OrderStatus.Preparing, PaymentDispatchTests.Now); }
            await check.SaveChangesAsync();
        };
        var config = Configuration();
        config.RefundEnabled = fault != "disabled";
        var result = await new PaymentRefundService(db, gateway, Options.Create(config), new PaymentDispatchTests.Clock()).SendAsync(request.PaymentId,
            fault == "stale" ? Guid.NewGuid() : request.ConcurrencyVersion, fault == "actor" ? Actor with { UserId = Guid.NewGuid() } : Actor);
        Assert.Equal(expected, result);
        Assert.Equal(0, gateway.Posts);
        Assert.Equal(PaymentRefundRequestState.Prepared, (await db.PaymentRefundRequests.AsNoTracking().SingleAsync()).State);
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("payment")]
    [InlineData("unsent")]
    [InlineData("changed-order")]
    public async Task DivergentRefundRequiresReviewAndDoesNotRestock(string fault)
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        if (fault != "unsent") { await Service(db, new()).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor); }
        if (fault == "changed-order")
        {
            (await db.Orders.SingleAsync()).TransitionTo(OrderStatus.Preparing, PaymentDispatchTests.Now);
            await db.SaveChangesAsync();
        }
        var observed = Refunded().Observation!;
        if (fault == "amount") { observed = observed with { Refund = observed.Refund! with { Amount = 1m } }; }
        if (fault == "payment") { observed = observed with { Refund = observed.Refund! with { PaymentId = "PAY-other" } }; }
        await PaymentWebhookTests.Service(db, new() { Result = new(PaymentOrderQueryStatus.Found, observed) })
            .HandleAsync(PaymentWebhookTests.Request("refund-divergent"));
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(PaymentRefundRequestState.RequiresAttention, (await db.PaymentRefundRequests.SingleAsync()).State);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Single(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentSubmissionsHaveOnlyOnePost()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        var gateway = new Gateway();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var other = app.Services.CreateScope();
            return await Service(other.ServiceProvider.GetRequiredService<SallvatDbContext>(), gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor);
        }));
        Assert.All(results, r => Assert.Equal(PaymentRefundResult.AwaitingConfirmation, r));
        Assert.Equal(1, gateway.Posts);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == "payment.refund.dispatch.started").ToListAsync());
    }

    [Fact]
    public async Task ConfirmationReadCannotOverwriteAConcurrentFinancialReview()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var request = await SeedAsync(db);
        await Service(db, new()).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor);
        request = await db.PaymentRefundRequests.AsNoTracking().SingleAsync();
        var gateway = new Gateway
        {
            Result = Refunded(),
            DuringGet = async () =>
        {
            using var other = app.Services.CreateScope();
            var check = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            (await check.Payments.SingleAsync()).RequireCanonicalReview(PaymentAttentionReason.FinancialReview, PaymentDispatchTests.Now);
            await check.SaveChangesAsync();
        }
        };
        Assert.Equal(PaymentRefundResult.Conflict, await Service(db, gateway).CheckAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(PaymentRefundRequestState.AwaitingConfirmation, (await db.PaymentRefundRequests.SingleAsync()).State);
        Assert.Equal(0, gateway.Posts);
    }

    [Fact]
    public async Task PersistedSendingAfterRestartCanOnlyBeQueriedEvenIfNoPostReachedProvider()
    {
        await using var app = new AccountWebApplicationFactory();
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await SeedAsync(db);
        var request = await db.PaymentRefundRequests.SingleAsync();
        request.Begin(PaymentDispatchTests.Now);
        await db.SaveChangesAsync();
        var gateway = new Gateway();
        using var restart = app.Services.CreateScope();
        var restarted = restart.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(restarted, gateway).SendAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        Assert.Equal(PaymentRefundResult.AwaitingConfirmation, await Service(restarted, gateway).CheckAsync(request.PaymentId, request.ConcurrencyVersion, Actor));
        Assert.Equal(0, gateway.Posts);
        Assert.Equal(PaymentRefundRequestState.Sending, (await restarted.PaymentRefundRequests.SingleAsync()).State);
        await PaymentWebhookTests.AssertConfirmedAsync(restarted);
    }

    internal static PaymentOrderQueryResult Refunded() => new(PaymentOrderQueryStatus.Found,
        PaymentWebhookTests.Observation() with { SettledPaymentId = null, Refund = new("REF-total", "PAY-webhook", 158.50m) });
    internal static MercadoPagoOptions Configuration()
    {
        var options = PaymentRefundPreparationTests.Configuration();
        options.RefundEnabled = true;
        return options;
    }
    internal static PaymentRefundService Service(SallvatDbContext db, Gateway gateway) => new(db, gateway, Options.Create(Configuration()), new PaymentDispatchTests.Clock());
    internal static async Task<PaymentRefundRequest> SeedAsync(SallvatDbContext db)
    {
        var (payment, order) = await PaymentRefundPreparationTests.SeedAsync(db);
        Assert.Equal(PaymentRefundPreparationStatus.Prepared, (await PaymentRefundPreparationTests.Service(db)
            .PrepareAsync(payment.Id, payment.ConcurrencyVersion, order.ConcurrencyVersion, PaymentRefundPreparationTests.Operation)).Status);
        db.ChangeTracker.Clear();
        return await db.PaymentRefundRequests.AsNoTracking().SingleAsync();
    }
    internal static async Task AssertRefundedAsync(SallvatDbContext db)
    {
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentRefundRequestState.Confirmed, (await db.PaymentRefundRequests.SingleAsync()).State);
        Assert.Equal("REF-total", (await db.PaymentRefundRequests.SingleAsync()).ExternalRefundId);
        Assert.Equal(PaymentStatus.Refunded, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Refunded, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(0, (await db.ProductVariants.SingleAsync()).Reserved);
        Assert.Single(await db.InventoryMovements.ToListAsync());
    }
    internal sealed class Gateway : IPaymentGateway
    {
        private int posts;
        public int Posts => posts;
        public PaymentRefundSubmit? Submitted { get; private set; }
        public bool ThrowOnPost { get; init; }
        public bool PostTokenCancelled { get; private set; }
        public Func<Task>? DuringGet { get; set; }
        public Func<Task>? DuringPost { get; init; }
        public PaymentOrderQueryResult Result { get; set; } = new(PaymentOrderQueryStatus.Found, PaymentWebhookTests.Observation());
        public async Task<PaymentOrderQueryResult> GetOrderAsync(PaymentOrderQuery request, CancellationToken cancellationToken = default)
        {
            if (DuringGet is not null) { await DuringGet(); }
            return Result;
        }
        public async Task<PaymentRefundSubmitResult> RefundOrderAsync(PaymentRefundSubmit request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref posts);
            Submitted = request;
            if (DuringPost is not null) { await DuringPost(); }
            PostTokenCancelled = cancellationToken.IsCancellationRequested;
            if (ThrowOnPost) { throw new HttpRequestException("Simulated timeout after acceptance"); }
            return PaymentRefundSubmitResult.Accepted;
        }
        public Task<PaymentOrderResult> CreateOrderAsync(PaymentOrderRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(PaymentPreferenceRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
