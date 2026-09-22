using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class MercadoPagoOrderGatewayTests
{
    private const string OrderId = "ORDTST01KS5AJ6HTK2HRQ3XJ3C2JCKP9";
    private const string CheckoutUrl = "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=" + OrderId;
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendsOrdersContractWithExactNetLinesAndNoPayerOrPreferenceFields()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            using var handler = new Handler((_, _) => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var request = Request();
            var result = await Service(client).CreateOrderAsync(request);
            Assert.Equal(PaymentOrderStatus.Created, result.Status);
            Assert.Equal(OrderId, result.ExternalOrderId);
            Assert.Equal(CheckoutUrl, result.CheckoutUrl!.AbsoluteUri);
            Assert.Equal(1, handler.Calls);
            Assert.Equal("https://api.mercadopago.com/v1/orders", handler.Url);
            Assert.Equal(request.IdempotencyKey.ToString("D"), handler.Key);
            Assert.Equal("Bearer APP_USR-test-only", handler.Authorization);
            using var json = JsonDocument.Parse(handler.Body!);
            var root = json.RootElement;
            Assert.Equal("online", root.GetProperty("type").GetString());
            Assert.Equal("manual", root.GetProperty("processing_mode").GetString());
            Assert.Equal("158.49", root.GetProperty("total_amount").GetString());
            Assert.Equal("PT30M", root.GetProperty("expiration_time").GetString());
            var lines = root.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, lines.Length);
            Assert.Equal("69.99", lines[0].GetProperty("unit_price").GetString());
            Assert.Equal("70.00", lines[1].GetProperty("unit_price").GetString());
            Assert.Equal("18.50", lines[2].GetProperty("unit_price").GetString());
            Assert.Equal("shipping", lines[2].GetProperty("external_code").GetString());
            var online = root.GetProperty("config").GetProperty("online");
            Assert.Equal("https://staging.example.com/pagamentos/retorno/sucesso", online.GetProperty("success_url").GetString());
            Assert.Equal("https://staging.example.com/pagamentos/retorno/pendente", online.GetProperty("pending_url").GetString());
            Assert.Equal("https://staging.example.com/pagamentos/retorno/falha", online.GetProperty("failure_url").GetString());
            foreach (var absent in new[] { "payer", "shipments", "back_urls", "notification_url", "payment_methods" })
            {
                Assert.False(root.TryGetProperty(absent, out _));
            }

            Assert.DoesNotContain("client-secret", result.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("user_id", "999")]
    [InlineData("currency", "USD")]
    [InlineData("country_code", "ARG")]
    [InlineData("external_reference", "another-order")]
    [InlineData("total_amount", "158.48")]
    [InlineData("total_amount", "158,49")]
    [InlineData("total_paid_amount", "0.01")]
    [InlineData("status", "processed")]
    [InlineData("type", "point")]
    [InlineData("processing_mode", "automatic")]
    [InlineData("id", "123-preference")]
    [InlineData("id", "ORD<unsafe>")]
    [InlineData("checkout_url", "https://evil.example/checkout/v1/redirect")]
    [InlineData("checkout_url", "https://www.mercadopago.com.br.evil.example/checkout/v1/redirect")]
    [InlineData("checkout_url", "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=other")]
    [InlineData("checkout_url", CheckoutUrl + "#fragment")]
    [InlineData("checkout_url", CheckoutUrl + "&next=evil")]
    [InlineData("checkout_url", "http://www.mercadopago.com.br/checkout/v1/redirect?order_id=" + OrderId)]
    [InlineData("checkout_url", "https://user@www.mercadopago.com.br/checkout/v1/redirect?order_id=" + OrderId)]
    [InlineData("checkout_url", "https://www.mercadopago.com.br:444/checkout/v1/redirect?order_id=" + OrderId)]
    public async Task RejectsDivergentOrUnsafeResponses(string field, string value)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(field, value)));
        using var client = new HttpClient(handler);
        var result = await Service(client).CreateOrderAsync(Request());
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, result.Status);
        Assert.Null(result.ExternalOrderId);
        Assert.Null(result.CheckoutUrl);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400, PaymentOrderStatus.Rejected)]
    [InlineData(422, PaymentOrderStatus.Rejected)]
    [InlineData(401, PaymentOrderStatus.AuthenticationFailure)]
    [InlineData(403, PaymentOrderStatus.AuthenticationFailure)]
    [InlineData(409, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(423, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(429, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(408, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(500, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(302, PaymentOrderStatus.OutcomeUnknown)]
    [InlineData(200, PaymentOrderStatus.OutcomeUnknown)]
    public async Task NeverRetriesOrFallsBackOnProviderFailure(int code, PaymentOrderStatus expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
        {
            Content = new StringContent("provider-secret"),
        }));
        using var client = new HttpClient(handler);
        var result = await Service(client).CreateOrderAsync(Request());
        Assert.Equal(expected, result.Status);
        Assert.Null(result.ExternalOrderId);
        Assert.Null(result.CheckoutUrl);
        Assert.DoesNotContain("provider-secret", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvalidInputsNeverReachTheProvider()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        var valid = Request();
        PaymentOrderRequest[] requests =
        [
            valid with { Amount = 158.48m }, valid with { Amount = 0 }, valid with { Amount = decimal.MaxValue },
            valid with { Currency = "USD" }, valid with { Environment = PaymentEnvironment.Production },
            valid with { IdempotencyKey = Guid.Empty }, valid with { ExternalReference = "email@example.com" },
            valid with { ExpiresAtUtc = Now }, valid with { ExpiresAtUtc = Now.AddMilliseconds(999) },
            valid with { ExpiresAtUtc = Now.AddDays(2) },
            valid with { ExpiresAtUtc = valid.ExpiresAtUtc.ToOffset(TimeSpan.FromHours(-3)) },
            valid with { Items = [] }, valid with { Items = null! },
            valid with { Items = [new("sku", "Perfume", 0, 158.49m)] },
            valid with { Items = [new("sku", "Perfume\n", 1, 158.49m)] },
            valid with { Items = [new("sku", "Perfume", 1, 158.491m)] },
            valid with { Items = [new("sku", "Perfume", 1, decimal.MaxValue)] },
            valid with { Items = [new("same", "Perfume", 1, 140m), new("same", "Frete", 1, 18.49m)] },
        ];
        foreach (var request in requests)
        {
            Assert.Equal(PaymentOrderStatus.InvalidRequest, (await Service(client).CreateOrderAsync(request)).Status);
        }

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task FlagsAreIndependentMutuallyExclusiveAndDisabledByDefault()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        var options = Options();
        options.OrdersEnabled = false;
        options.Enabled = true;
        Assert.Equal(PaymentOrderStatus.Disabled, (await Service(client, options).CreateOrderAsync(Request())).Status);
        options.OrdersEnabled = true;
        Assert.Equal(PaymentOrderStatus.ConfigurationInvalid, (await Service(client, options).CreateOrderAsync(Request())).Status);
        options.Enabled = false;
        options.TestSellerConfirmed = false;
        Assert.Equal(PaymentOrderStatus.ConfigurationInvalid, (await Service(client, options).CreateOrderAsync(Request())).Status);
        options = Options();
        options.Environment = PaymentEnvironment.Production;
        Assert.Equal(PaymentOrderStatus.ConfigurationInvalid, (await Service(client, options).CreateOrderAsync(Request())).Status);
        options = Options();
        options.AccessToken = "unsafe\r\nheader";
        Assert.Equal(PaymentOrderStatus.ConfigurationInvalid, (await Service(client, options).CreateOrderAsync(Request())).Status);
        Assert.Equal(0, handler.Calls);
        await using var application = new SallvatWebApplicationFactory();
        var gateway = application.Services.GetRequiredService<IPaymentGateway>();
        Assert.Equal(PaymentOrderStatus.Disabled, (await gateway.CreateOrderAsync(Request())).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{broken")]
    public async Task MalformedResponsesAreUnknown(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(body) }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(client).CreateOrderAsync(Request())).Status);
    }

    [Fact]
    public async Task OversizedAndLateResponsesAreUnknown()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(new string('a', 65_537)),
        }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(client).CreateOrderAsync(Request())).Status);
        var clock = new MutableClock();
        using var lateHandler = new Handler((_, _) =>
        {
            clock.UtcNow = Now.AddMinutes(30);
            return Task.FromResult(Response());
        });
        using var lateClient = new HttpClient(lateHandler);
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(lateClient, clock: clock).CreateOrderAsync(Request())).Status);
    }

    [Fact]
    public async Task TransportAndCancellationAfterSendNeverRetry()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("private-detail"));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(client).CreateOrderAsync(Request())).Status);
        Assert.Equal(1, handler.Calls);
        using var cancellation = new CancellationTokenSource();
        using var cancelledHandler = new Handler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response());
        });
        using var cancelledClient = new HttpClient(cancelledHandler);
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(cancelledClient).CreateOrderAsync(Request(), cancellation.Token)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(cancelledClient).CreateOrderAsync(Request(), cancellation.Token));
        Assert.Equal(1, cancelledHandler.Calls);
    }

    [Fact]
    public async Task TimeoutDoesNotCreateAnotherOrder()
    {
        using var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        using var client = new HttpClient(handler);
        var options = Options();
        options.TimeoutSeconds = 2;
        Assert.Equal(PaymentOrderStatus.OutcomeUnknown, (await Service(client, options).CreateOrderAsync(Request())).Status);
        Assert.Equal(1, handler.Calls);
    }

    private static PaymentOrderRequest Request() => new(Guid.NewGuid(), PaymentEnvironment.Sandbox,
        "SVT-20260922-00001000", 158.49m, "BRL", Now.AddMinutes(30),
        [new("sku-50-a", "Perfume — 50 ml", 1, 69.99m), new("sku-50-b", "Perfume — 50 ml", 1, 70m), new("shipping", "Frete", 1, 18.50m)]);

    private static MercadoPagoOptions Options() => new()
    {
        OrdersEnabled = true,
        AccessToken = "APP_USR-test-only",
        TestSellerId = 123,
        TestSellerConfirmed = true,
        PublicOrigin = "https://staging.example.com",
    };

    private static MercadoPagoPaymentGateway Service(HttpClient client, MercadoPagoOptions? options = null, IClock? clock = null) =>
        new(client, Microsoft.Extensions.Options.Options.Create(options ?? Options()), clock ?? new MutableClock());

    private static HttpResponseMessage Response(string? field = null, string? value = null)
    {
        var body = new Dictionary<string, string?>
        {
            ["id"] = OrderId,
            ["type"] = "online",
            ["processing_mode"] = "manual",
            ["status"] = "created",
            ["user_id"] = "123",
            ["currency"] = "BRL",
            ["country_code"] = "BRA",
            ["total_amount"] = "158.49",
            ["total_paid_amount"] = "0.00",
            ["external_reference"] = "SVT-20260922-00001000",
            ["checkout_url"] = CheckoutUrl,
            ["client_token"] = "client-secret",
        };
        if (field is not null)
        {
            body[field] = value;
        }

        return new(HttpStatusCode.Created) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Url { get; private set; }
        public string? Key { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Url = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.GetValues("X-Idempotency-Key").Single();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }
}
