using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sallvat.Application.Orders;
using Sallvat.Application.Payments;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Orders;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentWebhookTests
{
    internal const string Secret = "sandbox-webhook-secret-for-tests-only";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignatureSupportsExplicitSecondsAndMilliseconds(bool milliseconds)
    {
        Assert.NotNull(MercadoPagoWebhookSignature.Verify(Request(milliseconds: milliseconds), Secret, PaymentDispatchTests.Now));
        Assert.Null(MercadoPagoWebhookSignature.Verify(Request(milliseconds: milliseconds), "wrong-secret", PaymentDispatchTests.Now));
        var options = Configuration();
        Assert.True(MercadoPagoOptions.IsValid(options));
        options.WebhookSecret = "short";
        Assert.False(MercadoPagoOptions.IsValid(options));
        options = Configuration();
        options.OrdersEnabled = false;
        Assert.False(MercadoPagoOptions.IsValid(options));
    }

    [Fact]
    public async Task SignedHttpNotificationConfirmsThroughRealServiceAndDuplicateIsHarmless()
    {
        await using var app = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        var gateway = new Gateway();
        await using var configured = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
            services.PostConfigure<MercadoPagoOptions>(options =>
            {
                options.OrdersEnabled = true;
                options.WebhookEnabled = true;
                options.WebhookSecret = Secret;
                options.AccessToken = "test-only";
                options.TestSellerConfirmed = true;
                options.TestSellerId = 123;
                options.PublicOrigin = "https://staging.example.com";
            });
        }));
        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var first = await client.SendAsync(HttpRequest());
        using var duplicate = await client.SendAsync(HttpRequest());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(1, gateway.Calls);
        await AssertConfirmedAsync(db);
    }

    [Fact]
    public async Task CancellationDuringCanonicalQueryWinsWithoutConsumingReleasedReservation()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock();
        var gateway = new Gateway
        {
            DuringQuery = async () =>
            {
                clock.UtcNow = PaymentDispatchTests.Now.AddMinutes(30);
                using var other = app.Services.CreateScope();
                await new OrderLifecycleService(other.ServiceProvider.GetRequiredService<SallvatDbContext>(), clock).ExpirePendingAsync(10);
            },
        };
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, gateway, clock).HandleAsync(Request()));
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(0, (await db.ProductVariants.SingleAsync()).Reserved);
    }

    [Fact]
    public async Task ConcurrentDeliveriesAndDifferentReceiptsConsumeStockOnlyOnce()
    {
        await using var app = await CreateAsync();
        var gateway = new Gateway();
        var request = Request();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = app.Services.CreateScope();
            return await Service(scope.ServiceProvider.GetRequiredService<SallvatDbContext>(), gateway).HandleAsync(request);
        }));
        Assert.All(results, r => Assert.Equal(PaymentWebhookResult.Accepted, r));
        using var read = app.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(1, await db.WebhookEvents.CountAsync());
        var calls = gateway.Calls;
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, gateway).HandleAsync(request));
        Assert.Equal(calls, gateway.Calls);
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, gateway).HandleAsync(Request("another-delivery")));
        await AssertConfirmedAsync(db);
        Assert.Equal(2, await db.WebhookEvents.CountAsync());
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("request-id")]
    [InlineData("data-id")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("duplicate-ts")]
    [InlineData("duplicate-v1")]
    public async Task InvalidSignatureCannotReadOrWrite(string mutation)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var original = Request();
        var request = mutation switch
        {
            "signature" => original with { Signature = "ts=1,v1=invalid" },
            "request-id" => original with { RequestId = "different" },
            "data-id" => original with { DataId = "ORD-other" },
            "expired" => Request(now: PaymentDispatchTests.Now.AddMinutes(-6)),
            "future" => Request(now: PaymentDispatchTests.Now.AddMinutes(6)),
            "duplicate-ts" => original with { Signature = original.Signature + ",ts=1" },
            _ => original with { Signature = original.Signature + ",v1=" + new string('a', 64) },
        };
        var gateway = new Gateway();
        Assert.Equal(PaymentWebhookResult.Unauthorized, await Service(db, gateway).HandleAsync(request));
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"type\":\"order\",\"data\":{\"id\":\"ORD-other\"}}")]
    [InlineData("{\"type\":\"order\",\"type\":\"order\",\"data\":{\"id\":\"ORD-webhook\"}}")]
    public async Task InvalidEnvelopeNeverCallsProvider(string body)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var gateway = new Gateway();
        var request = Request() with { Body = Encoding.UTF8.GetBytes(body) };
        Assert.Equal(PaymentWebhookResult.Invalid, await Service(scope.ServiceProvider.GetRequiredService<SallvatDbContext>(), gateway).HandleAsync(request));
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task UnsignedStatusAndEventIdNeverApproveOrPoisonDeduplication()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var gateway = new Gateway
        {
            Result = new(PaymentOrderQueryStatus.Found, Observation() with
            { State = ObservedOrderState.Created, PaidAmount = 0, HasTransactions = false, SettledPaymentId = null })
        };
        var request = Request() with { Body = Encoding.UTF8.GetBytes("{\"type\":\"order\",\"id\":\"fake\",\"status\":\"approved\",\"data\":{\"id\":\"ORD-webhook\"}}") };
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, gateway).HandleAsync(request));
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(WebhookOutcome.Observed, (await db.WebhookEvents.SingleAsync()).Outcome);
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, gateway).HandleAsync(request with { Body = Request().Body }));
        Assert.Equal(1, gateway.Calls);
    }

    [Theory]
    [InlineData(PaymentOrderQueryStatus.Unavailable)]
    [InlineData(PaymentOrderQueryStatus.NotFound)]
    [InlineData(PaymentOrderQueryStatus.AuthenticationFailure)]
    public async Task TransientFailureKeepsDeliveryRetryable(PaymentOrderQueryStatus failure)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        Assert.Equal(PaymentWebhookResult.Retry, await Service(db, new Gateway { Result = new(failure) }).HandleAsync(Request()));
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, new Gateway()).HandleAsync(Request()));
        await AssertConfirmedAsync(db);
    }

    [Fact]
    public async Task UnknownIdAndDisabledFlagCannotCreateOrAssociateAttempt()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var gateway = new Gateway();
        Assert.Equal(PaymentWebhookResult.Retry, await Service(db, gateway).HandleAsync(Request(id: "ORD-unknown")));
        Assert.Equal(PaymentWebhookResult.Disabled, await new PaymentWebhookService(db, gateway,
            Options.Create(PaymentDispatchTests.Configuration()), new PaymentDispatchTests.Clock()).HandleAsync(Request()));
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.WebhookEvents.ToListAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredOrCancelledOrderNeverConsumesStock(bool cancel)
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var clock = new PaymentDispatchTests.Clock { UtcNow = PaymentDispatchTests.Now.AddMinutes(30) };
        if (cancel)
        {
            Assert.Equal(1, await new OrderLifecycleService(db, clock).ExpirePendingAsync(10));
        }

        Assert.Equal(PaymentWebhookResult.Accepted, await Service(db, new Gateway(), clock).HandleAsync(Request(now: clock.UtcNow)));
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(PaymentAttentionReason.LateApproval, (await db.Payments.SingleAsync()).AttentionReason);
        Assert.Equal(cancel ? OrderStatus.Cancelled : OrderStatus.RequiresAttention, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(4, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.False(await db.InventoryMovements.AnyAsync(m => m.Type == InventoryMovementType.Sale));
    }

    [Fact]
    public async Task RefundOrMismatchRequiresReviewAndOldObservationNeverRegressesCapture()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await Service(db, new Gateway()).HandleAsync(Request());
        var old = Observation() with { UpdatedAtUtc = PaymentDispatchTests.Now.AddSeconds(-1), SettledPaymentId = null, State = ObservedOrderState.Other };
        await Service(db, new Gateway { Result = new(PaymentOrderQueryStatus.Found, old) }).HandleAsync(Request("old"));
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        await Service(db, new Gateway { Result = new(PaymentOrderQueryStatus.InvalidResponse) }).HandleAsync(Request("mismatch"));
        Assert.Equal(PaymentStatus.RequiresAttention, (await db.Payments.SingleAsync()).Status);
        Assert.Equal("PAY-webhook", (await db.Payments.SingleAsync()).ExternalPaymentId);
        Assert.Equal(OrderStatus.RequiresAttention, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.Type == InventoryMovementType.Sale));
        var order = await db.Orders.SingleAsync();
        var cancel = await new OrderLifecycleService(db, new PaymentDispatchTests.Clock()).TransitionAsync(order.Id,
            order.ConcurrencyVersion, OrderStatus.Cancelled, "Cancelamento sem estorno", new(Guid.NewGuid(), "test-capture-guard"));
        Assert.Equal(OrderLifecycleMutationStatus.Invalid, cancel.Status);
        Assert.Equal(OrderStatus.RequiresAttention, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task HttpEndpointRequiresHttpsBoundsBodyAndExemptsOnlyWebhookFromAntiforgery()
    {
        await using var app = new SallvatWebApplicationFactory();
        var fake = new EndpointService();
        await using var configured = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentWebhookService>();
            services.AddSingleton<IPaymentWebhookService>(fake);
        }));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var accepted = await client.SendAsync(HttpRequest());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, fake.Calls);
        using var missing = new HttpRequestMessage(HttpMethod.Post, "/integracoes/mercado-pago/webhook") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        using var invalid = await client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var large = HttpRequest();
        large.Content = new StringContent(new string('x', 16_385), Encoding.UTF8, "application/json");
        using var tooLarge = await client.SendAsync(large);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        using var http = HttpRequest("http://localhost/integracoes/mercado-pago/webhook?data.id=ORD-webhook");
        using var insecure = await client.SendAsync(http);
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        Assert.Equal(1, fake.Calls);
    }

    internal static async Task SeedAsync(SallvatDbContext db)
    {
        await PaymentPreparationTests.SeedAsync(db);
        await new PaymentPreparationService(db, new PaymentDispatchTests.Clock()).PrepareAsync(1_000, PaymentDispatchTests.Owner, PaymentEnvironment.Sandbox);
        var payment = await db.Payments.SingleAsync();
        var token = Guid.NewGuid();
        payment.TryBeginOrderDispatch(token, PaymentDispatchTests.Now);
        payment.CompleteOrderDispatch(token, "ORD-webhook", true, PaymentDispatchTests.Now);
        await db.SaveChangesAsync();
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync()
    {
        var app = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<SallvatDbContext>());
        return app;
    }

    internal static PaymentWebhookService Service(SallvatDbContext db, Gateway gateway, PaymentDispatchTests.Clock? clock = null) =>
        new(db, gateway, Options.Create(Configuration()), clock ?? new PaymentDispatchTests.Clock());

    internal static MercadoPagoOptions Configuration()
    {
        var options = PaymentDispatchTests.Configuration();
        options.WebhookEnabled = true;
        options.WebhookSecret = Secret;
        return options;
    }

    internal static PaymentWebhookRequest Request(string requestId = "request-123", DateTimeOffset? now = null, string id = "ORD-webhook", bool milliseconds = false)
    {
        var time = now ?? PaymentDispatchTests.Now;
        var ts = (milliseconds ? time.ToUnixTimeMilliseconds() : time.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"id:{id};request-id:{requestId};ts:{ts};"))).ToLowerInvariant();
        return new(id, requestId, $"ts={ts},v1={hash}", JsonSerializer.SerializeToUtf8Bytes(new { type = "order", data = new { id } }));
    }

    internal static async Task AssertConfirmedAsync(SallvatDbContext db)
    {
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        Assert.Equal("PAY-webhook", (await db.Payments.SingleAsync()).ExternalPaymentId);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(StockReservationStatus.Consumed, (await db.StockReservations.SingleAsync()).Status);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync()).OnHand);
        Assert.Equal(0, (await db.ProductVariants.SingleAsync()).Reserved);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.Type == InventoryMovementType.Sale));
    }

    private static HttpRequestMessage HttpRequest(string url = "/integracoes/mercado-pago/webhook?data.id=ORD-webhook")
    {
        var request = Request();
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(Encoding.UTF8.GetString(request.Body), Encoding.UTF8, "application/json") };
        message.Headers.Add("x-request-id", request.RequestId);
        message.Headers.Add("x-signature", request.Signature);
        return message;
    }

    internal static PaymentOrderObservation Observation() => new("ORD-webhook", ObservedOrderState.Processed, 158.50m,
        PaymentDispatchTests.Now, PaymentDispatchTests.Now, true, "PAY-webhook");

    internal sealed class Gateway : IPaymentGateway
    {
        private int calls;
        public int Calls => calls;
        public PaymentOrderQueryResult Result { get; init; } = new(PaymentOrderQueryStatus.Found, Observation());
        public Func<Task>? DuringQuery { get; init; }
        public async Task<PaymentOrderQueryResult> GetOrderAsync(PaymentOrderQuery request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            if (DuringQuery is not null)
            {
                await DuringQuery();
            }

            return Result;
        }

        public Task<PaymentOrderResult> CreateOrderAsync(PaymentOrderRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No POST from webhook.");
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(PaymentPreferenceRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No preference from webhook.");
    }

    private sealed class EndpointService : IPaymentWebhookService
    {
        public int Calls { get; private set; }
        public Task<PaymentWebhookResult> HandleAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(PaymentWebhookResult.Accepted);
        }
    }
}
