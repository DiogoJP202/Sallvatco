using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Payments;
using Sallvat.Application.Promotions;
using Sallvat.Application.Shipping;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Shipping;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Payments;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Checkout;

public sealed partial class CheckoutPaymentFlowTests
{
    [Fact]
    public async Task GuestFlowReplaysWithoutDuplicateOrderOrDispatchAndOnlyWebhookConfirms()
    {
        await using var scenario = await Scenario.CreateAsync();
        var review = await scenario.ReviewAsync();
        Assert.DoesNotContain("cliente@example.com", Hidden(review, "reviewToken"), StringComparison.Ordinal);
        var path = await scenario.ConfirmAsync(review);
        Assert.Equal(path, await scenario.ConfirmAsync(review));
        using var scope = scenario.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var order = await db.Orders.SingleAsync();
        Assert.Equal(318.40m, order.GrandTotal);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Single(await db.StockReservations.ToListAsync());
        Assert.Empty(await db.CartItems.ToListAsync());
        var status = await scenario.Client.GetAsync(path);
        Assert.True(status.Headers.CacheControl!.NoStore);
        var html = await status.Content.ReadAsStringAsync();
        Assert.Contains("noindex", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cliente@example.com", html, StringComparison.Ordinal);
        for (var i = 0; i < 2; i++)
        {
            var redirect = await scenario.Client.PostAsync(path + "/continuar", Form(html, ("confirmed", "true")));
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Equal("https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORD-dispatch", redirect.Headers.Location!.AbsoluteUri);
        }
        Assert.Equal(1, scenario.Gateway.Calls);
        Assert.Equal(order.CheckoutAttemptId, scenario.Gateway.Request!.CheckoutAttemptId);
        foreach (var outcome in new[] { "sucesso", "pendente", "falha" })
        {
            var returned = await scenario.Client.GetStringAsync($"/pagamentos/retorno/{outcome}?tentativa={order.CheckoutAttemptId}&status=approved&payment_id=123&collection_status=approved");
            Assert.Contains("data-payment-state=\"AwaitingPayment\"", returned, StringComparison.Ordinal);
        }
        Assert.Equal(0, scenario.Gateway.Queries);
        db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.PendingPayment, (await db.Orders.SingleAsync()).Status);
        Assert.Null((await db.Payments.SingleAsync()).ExternalPaymentId);

        var webhook = scope.ServiceProvider.GetRequiredService<IPaymentWebhookService>();
        Assert.Equal(PaymentWebhookResult.Accepted, await webhook.HandleAsync(
            PaymentWebhookTests.Request(id: "ORD-dispatch", now: scenario.Clock.UtcNow)));
        var confirmed = await scenario.Client.GetStringAsync(path);
        Assert.Contains("data-payment-state=\"Confirmed\"", confirmed, StringComparison.Ordinal);
        Assert.DoesNotContain("Abrir pagamento de teste", confirmed, StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(StockReservationStatus.Consumed, (await db.StockReservations.SingleAsync()).Status);
        Assert.Equal(1, scenario.Gateway.Queries);
        Assert.Equal(1, scenario.Gateway.Calls);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("expired")]
    [InlineData("confirmation")]
    [InlineData("csrf")]
    [InlineData("owner")]
    public async Task InvalidConfirmationCannotCreateOrder(string mutation)
    {
        await using var scenario = await Scenario.CreateAsync();
        var review = await scenario.ReviewAsync();
        var token = Hidden(review, "reviewToken");
        var html = review;
        using var stranger = scenario.App.CreateClient(ClientOptions());
        var client = scenario.Client;
        if (mutation == "owner")
        {
            client = stranger;
            html = await stranger.GetStringAsync("/conta/entrar");
        }
        if (mutation == "expired") { scenario.Clock.UtcNow = scenario.Clock.UtcNow.AddMinutes(11); }
        var form = mutation == "csrf" ? new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["reviewToken"] = token,
            ["confirmed"] = "true",
        }) : Form(html, ("reviewToken", mutation == "token" ? token[..^10] : token),
            ("confirmed", mutation == "confirmation" ? "false" : "true"));
        var result = await client.PostAsync("/checkout/confirmar", form);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        await scenario.AssertNoOrderAsync();
    }

    [Theory]
    [InlineData("price")]
    [InlineData("quantity")]
    [InlineData("freight-price")]
    [InlineData("freight-days")]
    [InlineData("freight-carrier")]
    [InlineData("freight-failure")]
    public async Task ChangedReviewRequiresFreshConfirmationWithoutReservation(string mutation)
    {
        await using var scenario = await Scenario.CreateAsync();
        var review = await scenario.ReviewAsync();
        if (mutation is "price" or "quantity")
        {
            using var scope = scenario.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == scenario.Product.AvailableVariantId);
            if (mutation == "price")
            {
                variant.UpdateCommercialData(variant.Sku, variant.VolumeMl, 300m, variant.WeightKg,
                    variant.HeightCm, variant.WidthCm, variant.LengthCm, true, scenario.Clock.UtcNow);
            }
            else { (await db.CartItems.SingleAsync()).ChangeQuantity(2, scenario.Clock.UtcNow); }
            await db.SaveChangesAsync();
        }
        if (mutation == "freight-price") { scenario.Freight.Price++; }
        if (mutation == "freight-days") { scenario.Freight.Days++; }
        if (mutation == "freight-carrier") { scenario.Freight.Carrier = "Outra"; }
        if (mutation == "freight-failure") { scenario.Freight.Unavailable = true; }
        Assert.Equal("/checkout", await scenario.ConfirmAsync(review));
        await scenario.AssertNoOrderAsync();
    }

    [Fact]
    public async Task DisabledFlagBlocksBothPostsEvenWithValidAntiforgery()
    {
        await using var scenario = await Scenario.CreateAsync(enabled: false);
        var review = await scenario.ReviewAsync();
        Assert.DoesNotContain("name=\"reviewToken\"", review, StringComparison.Ordinal);
        var login = await scenario.Client.GetStringAsync("/conta/entrar");
        foreach (var path in new[] { "/checkout/confirmar", $"/pagamentos/{Guid.NewGuid()}/continuar" })
        {
            var response = await scenario.Client.PostAsync(path, Form(login, ("confirmed", "true"), ("reviewToken", "forged")));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        await scenario.AssertNoOrderAsync();
    }

    [Fact]
    public async Task ForeignAndExpiredGuestSessionsCannotReadOrPayAndMissingReturnLocatorIsNotFound()
    {
        await using var scenario = await Scenario.CreateAsync();
        var path = await scenario.ConfirmAsync(await scenario.ReviewAsync());
        using var stranger = scenario.App.CreateClient(ClientOptions());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        var login = await stranger.GetStringAsync("/conta/entrar");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(path + "/continuar", Form(login, ("confirmed", "true")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Client.GetAsync("/pagamentos/retorno/sucesso?status=approved")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Client.GetAsync("/pagamentos/retorno/anything?tentativa=" + path.Split('/').Last())).StatusCode);
        scenario.Clock.UtcNow = scenario.Clock.UtcNow.AddDays(31);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Client.GetAsync(path)).StatusCode);
        Assert.Equal(0, scenario.Gateway.Calls);
    }

    [Fact]
    public async Task UncertainDispatchCannotBeRetriedAndReturnRemainsUnderReview()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Gateway.Unknown = true;
        var path = await scenario.ConfirmAsync(await scenario.ReviewAsync());
        var html = await scenario.Client.GetStringAsync(path);
        for (var i = 0; i < 2; i++)
        {
            var result = await scenario.Client.PostAsync(path + "/continuar", Form(html, ("confirmed", "true")));
            Assert.Equal(path, result.Headers.Location!.OriginalString);
        }
        var returned = await scenario.Client.GetStringAsync(path);
        Assert.Contains("data-payment-state=\"RequiresAttention\"", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("Abrir pagamento de teste", returned, StringComparison.Ordinal);
        Assert.Equal(1, scenario.Gateway.Calls);
    }

    [Fact]
    public async Task PaymentPostRequiresAntiforgeryAndExplicitConfirmation()
    {
        await using var scenario = await Scenario.CreateAsync();
        var path = await scenario.ConfirmAsync(await scenario.ReviewAsync());
        var html = await scenario.Client.GetStringAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Client.PostAsync(path + "/continuar", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Client.PostAsync(path + "/continuar", Form(html))).StatusCode);
        Assert.Equal(0, scenario.Gateway.Calls);
    }

    [Fact]
    public async Task AddingCouponAfterReviewRequiresNewConfirmation()
    {
        await using var scenario = await Scenario.CreateAsync();
        var review = await scenario.ReviewAsync();
        using (var scope = scenario.App.Services.CreateScope())
        {
            var coupon = await scope.ServiceProvider.GetRequiredService<ICouponService>().CreateAsync(
                new CouponEditorInput("NOVA10", CouponDiscountType.Percentage, 10m, 0m, null, null, 10, 1, true),
                new(scenario.Product.ActorId, "checkout-test"));
            Assert.True(coupon.Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            (await db.Carts.SingleAsync()).ApplyCoupon((await db.Coupons.SingleAsync()).Id, scenario.Clock.UtcNow);
            await db.SaveChangesAsync();
        }
        Assert.Equal("/checkout", await scenario.ConfirmAsync(review));
        await scenario.AssertNoOrderAsync();
    }

    [Fact]
    public async Task PaymentActionsShareFivePerMinuteIpLimit()
    {
        await using var scenario = await Scenario.CreateAsync(enabled: false);
        var login = await scenario.Client.GetStringAsync("/conta/entrar");
        for (var index = 0; index < 5; index++)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await scenario.Client.PostAsync("/checkout/confirmar", Form(login))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await scenario.Client.PostAsync($"/pagamentos/{Guid.NewGuid()}/continuar", Form(login))).StatusCode);
        await scenario.AssertNoOrderAsync();
    }

    [Fact]
    public async Task CustomerResourceOwnershipDoesNotFallBackToGuestOrEmail()
    {
        await using var app = new AccountWebApplicationFactory(clock: new PaymentDispatchTests.Clock());
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var userId = Guid.NewGuid();
        await PaymentPreparationTests.SeedAsync(db, userId);
        var attempt = (await db.Orders.SingleAsync()).CheckoutAttemptId;
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
        Assert.NotNull(await service.GetAsync(attempt, CartOwner.ForCustomer(userId)));
        Assert.Null(await service.GetAsync(attempt, CartOwner.ForCustomer(Guid.NewGuid())));
        Assert.Null(await service.GetAsync(attempt, CartOwner.ForGuest(new string('A', 43))));
        Assert.Null(await service.GetAsync(attempt, new(new string('A', 43), userId)));
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("webhook")]
    [InlineData("recovery")]
    [InlineData("automatic")]
    [InlineData("production")]
    public void CheckoutRequiresAllSandboxPaymentProtections(string missing)
    {
        var options = PaymentDispatchTests.Configuration();
        options.CheckoutEnabled = true;
        options.WebhookEnabled = true;
        options.WebhookSecret = PaymentWebhookTests.Secret;
        options.RecoveryEnabled = true;
        options.AutomaticRecoveryEnabled = true;
        Assert.True(MercadoPagoOptions.IsValid(options));
        if (missing == "orders") { options.OrdersEnabled = false; }
        if (missing == "webhook") { options.WebhookEnabled = false; }
        if (missing == "recovery") { options.RecoveryEnabled = false; }
        if (missing == "automatic") { options.AutomaticRecoveryEnabled = false; }
        if (missing == "production") { options.Environment = PaymentEnvironment.Production; }
        Assert.False(MercadoPagoOptions.IsValid(options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrProductionFreightCannotEnableCheckout(bool production)
    {
        await using var scenario = await Scenario.CreateAsync();
        var review = await scenario.ReviewAsync();
        using var scope = scenario.App.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MelhorEnvioOptions>>().Value;
        if (production) { options.BaseUrl = "https://melhorenvio.com.br/"; }
        else { options.Enabled = false; }
        var response = await scenario.Client.PostAsync("/checkout/confirmar", Form(review,
            ("reviewToken", Hidden(review, "reviewToken")), ("confirmed", "true")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await scenario.AssertNoOrderAsync();
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        BaseAddress = new("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private static string Hidden(string html, string name)
    {
        var input = Inputs().Matches(html).Select(m => m.Value).First(v => v.Contains($"name=\"{name}\"", StringComparison.Ordinal));
        return WebUtility.HtmlDecode(Value().Match(input).Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(string html, params (string Name, string Value)[] values) =>
        new(values.Select(v => new KeyValuePair<string, string>(v.Name, v.Value))
            .Append(new("__RequestVerificationToken", Hidden(html, "__RequestVerificationToken"))));

    [GeneratedRegex("<input[^>]+>")]
    private static partial Regex Inputs();
    [GeneratedRegex("value=\"([^\"]*)\"")]
    private static partial Regex Value();

    private sealed class Scenario : IAsyncDisposable
    {
        public PaymentDispatchTests.Clock Clock { get; } = new();
        public Gateway Gateway { get; } = new();
        public Freight Freight { get; }
        public AccountWebApplicationFactory App { get; }
        public HttpClient Client { get; }
        public PublishedProductData Product { get; private set; } = null!;

        private Scenario(bool enabled)
        {
            Freight = new(Clock);
            App = new(clock: Clock, freightService: Freight, configureServices: services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(Gateway);
                services.PostConfigure<MercadoPagoOptions>(options =>
                {
                    options.CheckoutEnabled = enabled;
                    options.OrdersEnabled = true;
                    options.WebhookEnabled = true;
                    options.RecoveryEnabled = true;
                    options.AutomaticRecoveryEnabled = true;
                    options.WebhookSecret = PaymentWebhookTests.Secret;
                    options.AccessToken = "test-only";
                    options.TestSellerConfirmed = true;
                    options.TestSellerId = 123;
                    options.PublicOrigin = "https://staging.example.com";
                });
                services.PostConfigure<MelhorEnvioOptions>(options =>
                {
                    options.Enabled = true;
                    options.AccessToken = "test-only";
                    options.OriginPostalCode = "02320040";
                    options.SupportEmail = "test@example.com";
                });
            });
            Client = App.CreateClient(ClientOptions());
        }

        public static async Task<Scenario> CreateAsync(bool enabled = true)
        {
            var scenario = new Scenario(enabled);
            await scenario.App.InitializeDatabaseAsync();
            scenario.Product = await PublishedCatalogFixture.CreateAsync(scenario.App, "checkout-payment");
            var html = await scenario.Client.GetStringAsync("/perfumes/checkout-payment");
            var added = await scenario.Client.PostAsync("/carrinho/itens", Form(html,
                ("VariantId", scenario.Product.AvailableVariantId.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Quantity", "1")));
            Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
            return scenario;
        }

        public async Task<string> ReviewAsync()
        {
            var html = await Client.GetStringAsync("/checkout");
            var result = await Client.PostAsync("/checkout/revisar", Form(html,
                ("Form.BuyerName", "Cliente Teste"), ("Form.Email", "cliente@example.com"), ("Form.Phone", "11999998888"),
                ("Form.RecipientName", "Cliente Teste"), ("Form.PostalCode", "01310100"), ("Form.Street", "Avenida Paulista"),
                ("Form.Number", "1000"), ("Form.District", "Bela Vista"), ("Form.City", "São Paulo"), ("Form.StateCode", "SP")));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            return await result.Content.ReadAsStringAsync();
        }

        public async Task<string> ConfirmAsync(string review)
        {
            var result = await Client.PostAsync("/checkout/confirmar", Form(review,
                ("reviewToken", Hidden(review, "reviewToken")), ("confirmed", "true"), ("GrandTotal", "0.01")));
            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
            return result.Headers.Location!.OriginalString;
        }

        public async Task AssertNoOrderAsync()
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Empty(await db.StockReservations.ToListAsync());
            Assert.Empty(await db.Payments.ToListAsync());
            Assert.Equal(0, Gateway.Calls);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private sealed class Freight(PaymentDispatchTests.Clock clock) : IFreightService
    {
        public decimal Price { get; set; } = 18.50m;
        public int Days { get; set; } = 2;
        public string Carrier { get; set; } = "Correios";
        public bool Unavailable { get; set; }
        public Task<FreightQuoteResult> QuoteAsync(FreightQuoteRequest request, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable ? FreightQuoteResult.Failure(FreightQuoteStatus.Unavailable, "Indisponível")
                : FreightQuoteResult.Success([new("melhor-envio:2", Carrier, "SEDEX", Price, "BRL", Days, Days + 2, clock.UtcNow, clock.UtcNow.AddMinutes(10))]));
    }

    private sealed class Gateway : IPaymentGateway
    {
        public Task<PaymentRefundSubmitResult> RefundOrderAsync(PaymentRefundSubmit request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No refund from checkout.");
        public int Calls { get; private set; }
        public int Queries { get; private set; }
        public bool Unknown { get; set; }
        public PaymentOrderRequest? Request { get; private set; }
        public Task<PaymentOrderResult> CreateOrderAsync(PaymentOrderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            return Task.FromResult(Unknown ? new PaymentOrderResult(PaymentOrderStatus.OutcomeUnknown) : PaymentDispatchTests.Gateway.Success());
        }
        public Task<PaymentOrderQueryResult> GetOrderAsync(PaymentOrderQuery request, CancellationToken cancellationToken = default)
        {
            Queries++;
            return Task.FromResult(new PaymentOrderQueryResult(PaymentOrderQueryStatus.Found,
                new(request.ExternalOrderId, ObservedOrderState.Processed, request.Amount,
                    PaymentDispatchTests.Now, PaymentDispatchTests.Now, true, "PAY-checkout")));
        }
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(PaymentPreferenceRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Preferences are outside this flow.");
    }
}
