using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure;
using Sallvat.Infrastructure.Payments;

namespace Sallvat.IntegrationTests.Payments;

public sealed class MercadoPagoPaymentGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
    private const string Reference = "SVT-20260921-00001000";
    private const string CheckoutUrl = "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-preference";

    [Fact]
    public async Task SendsNetAmountsShippingStableKeyAndHttpsReturnUrlsWithoutPayerData()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        var request = Request();
        var result = await Service(client).CreatePreferenceAsync(request);

        Assert.Equal(PaymentPreferenceStatus.Created, result.Status);
        Assert.Equal("123-preference", result.PreferenceId);
        Assert.Equal(CheckoutUrl, result.CheckoutUrl!.AbsoluteUri);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.mercadopago.com/checkout/preferences", handler.Url);
        Assert.Equal("Bearer APP_USR-test-only", handler.Authorization);
        Assert.Equal(request.IdempotencyKey.ToString("D"), handler.Key);
        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(63m, item.GetProperty("unit_price").GetDecimal());
        Assert.Equal("BRL", item.GetProperty("currency_id").GetString());
        Assert.Equal(18.50m, root.GetProperty("shipments").GetProperty("cost").GetDecimal());
        Assert.Equal(Reference, root.GetProperty("external_reference").GetString());
        Assert.True(root.GetProperty("expires").GetBoolean());
        Assert.Equal(request.ExpiresAtUtc, root.GetProperty("expiration_date_to").GetDateTimeOffset());
        Assert.Equal(Now, root.GetProperty("expiration_date_from").GetDateTimeOffset());
        Assert.Equal("https://staging.example.com/pagamentos/retorno/sucesso", root.GetProperty("back_urls").GetProperty("success").GetString());
        Assert.False(root.TryGetProperty("payer", out _));
        Assert.False(root.TryGetProperty("payment_methods", out _));
        Assert.False(root.TryGetProperty("notification_url", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PaymentPreferenceStatus.Rejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, PaymentPreferenceStatus.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, PaymentPreferenceStatus.AuthenticationFailure)]
    [InlineData(HttpStatusCode.Forbidden, PaymentPreferenceStatus.AuthenticationFailure)]
    [InlineData(HttpStatusCode.Conflict, PaymentPreferenceStatus.OutcomeUnknown)]
    [InlineData(HttpStatusCode.TooManyRequests, PaymentPreferenceStatus.OutcomeUnknown)]
    [InlineData(HttpStatusCode.RequestTimeout, PaymentPreferenceStatus.OutcomeUnknown)]
    [InlineData(HttpStatusCode.InternalServerError, PaymentPreferenceStatus.OutcomeUnknown)]
    [InlineData(HttpStatusCode.Found, PaymentPreferenceStatus.OutcomeUnknown)]
    [InlineData(HttpStatusCode.OK, PaymentPreferenceStatus.OutcomeUnknown)]
    public async Task FailuresAreSanitizedAndNeverAutomaticallyRetried(HttpStatusCode code, PaymentPreferenceStatus status)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent("secret-provider-payload"),
        }));
        using var client = new HttpClient(handler);
        var result = await Service(client).CreatePreferenceAsync(Request());
        Assert.Equal(status, result.Status);
        Assert.Null(result.PreferenceId);
        Assert.Null(result.CheckoutUrl);
        Assert.DoesNotContain("secret-provider-payload", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DisabledOrInvalidConfigurationNeverSendsHttp()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentPreferenceStatus.Disabled,
            (await Service(client, new MercadoPagoOptions()).CreatePreferenceAsync(Request())).Status);
        var options = Options();
        options.Environment = PaymentEnvironment.Production;
        Assert.Equal(PaymentPreferenceStatus.ConfigurationInvalid,
            (await Service(client, options).CreatePreferenceAsync(Request())).Status);
        options = Options();
        options.TestSellerConfirmed = false;
        Assert.Equal(PaymentPreferenceStatus.ConfigurationInvalid,
            (await Service(client, options).CreatePreferenceAsync(Request())).Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task InvalidRequestsNeverSendHttp()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        var valid = Request();
        PaymentPreferenceRequest[] invalid =
        [
            valid with { Environment = PaymentEnvironment.Production },
            valid with { IdempotencyKey = Guid.Empty },
            valid with { ExternalReference = "email@example.com" },
            valid with { Amount = 144.51m },
            valid with { Amount = 0m },
            valid with { Currency = "USD" },
            valid with { ShippingTotal = -1m },
            valid with { ExpiresAtUtc = Now },
            valid with { ExpiresAtUtc = Now.AddMinutes(30).ToOffset(TimeSpan.FromHours(-3)) },
            valid with { Items = [] },
            valid with { Items = [new("sku", "Perfume", 0, 63m)] },
            valid with { Items = [new("sku", "Perfume", 2, 63.001m)] },
            valid with { Items = [new("sku", "Perfume\n", 2, 63m)] },
            valid with { Items = [new("sku", "Perfume", 2, decimal.MaxValue)] },
        ];
        foreach (var request in invalid)
        {
            Assert.Equal(PaymentPreferenceStatus.InvalidRequest,
                (await Service(client).CreatePreferenceAsync(request)).Status);
        }

        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("http://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-preference")]
    [InlineData("https://www.mercadopago.com.br.evil.example/checkout/v1/redirect?pref_id=123-preference")]
    [InlineData("https://evil.example/checkout/v1/redirect?pref_id=123-preference")]
    [InlineData("https://user@www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-preference")]
    [InlineData("https://www.mercadopago.com.br:444/checkout/v1/redirect?pref_id=123-preference")]
    [InlineData("https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=different")]
    [InlineData("https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-preference#fragment")]
    public async Task UntrustedCheckoutUrlsCannotBecomeRedirects(string url)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Response(url: url)));
        using var client = new HttpClient(handler);
        var result = await Service(client).CreatePreferenceAsync(Request());
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown, result.Status);
        Assert.Null(result.CheckoutUrl);
    }

    [Fact]
    public async Task DivergentSellerReferenceAndExpiredResponsesAreUnknown()
    {
        foreach (var response in new[] { Response(sellerId: 999), Response(reference: "another-order"), Response(id: "<unsafe>") })
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(response));
            using var client = new HttpClient(handler);
            Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
                (await Service(client).CreatePreferenceAsync(Request())).Status);
        }

        var clock = new MutableClock { UtcNow = Now };
        using var lateHandler = new RecordingHandler((_, _) =>
        {
            clock.UtcNow = Now.AddMinutes(30);
            return Task.FromResult(Response());
        });
        using var lateClient = new HttpClient(lateHandler);
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(lateClient, clock: clock).CreatePreferenceAsync(Request())).Status);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task InvalidResponseShapesAreUnknown(string body)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(body),
        }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(client).CreatePreferenceAsync(Request())).Status);
    }

    [Fact]
    public async Task OversizedResponseIsBoundedAndUnknown()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(new string('a', 65_537)),
        }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(client).CreatePreferenceAsync(Request())).Status);
    }

    [Fact]
    public async Task TransportFailureAndCancellationAfterSendingAreUnknownWithoutRetry()
    {
        using var failingHandler = new RecordingHandler((_, _) => throw new HttpRequestException("secret-network-detail"));
        using var failingClient = new HttpClient(failingHandler);
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(failingClient).CreatePreferenceAsync(Request())).Status);
        Assert.Equal(1, failingHandler.CallCount);
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response());
        });
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(client).CreatePreferenceAsync(Request(), cancellation.Token)).Status);
        Assert.Equal(1, handler.CallCount);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(client).CreatePreferenceAsync(Request(), cancellation.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TimeoutDoesNotRetryCreation()
    {
        using var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        using var client = new HttpClient(handler);
        var options = Options();
        options.TimeoutSeconds = 2;
        Assert.Equal(PaymentPreferenceStatus.OutcomeUnknown,
            (await Service(client, options).CreatePreferenceAsync(Request())).Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("http://staging.example.com")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://user:pass@staging.example.com")]
    [InlineData("https://staging.example.com/path")]
    [InlineData("https://staging.example.com?query=secret")]
    [InlineData("https://staging.example.com#fragment")]
    public void RejectsUnsafeOrigins(string origin)
    {
        var options = Options();
        options.PublicOrigin = origin;
        Assert.False(MercadoPagoOptions.IsValid(options));
    }

    [Fact]
    public async Task DependencyInjectionDefaultsToDisabledAndValidatesProductionConfiguration()
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:SallvatDatabase"] = "design-only" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(PaymentPreferenceStatus.Disabled,
            (await provider.GetRequiredService<IPaymentGateway>().CreatePreferenceAsync(Request())).Status);
        var options = Options();
        Assert.True(MercadoPagoOptions.IsValid(options));
        options.Environment = PaymentEnvironment.Production;
        Assert.False(MercadoPagoOptions.IsValid(options));
        options = Options();
        options.AccessToken = "token\r\nInjected: header";
        Assert.False(MercadoPagoOptions.IsValid(options));
    }

    private static MercadoPagoOptions Options() => new()
    {
        Enabled = true,
        AccessToken = "APP_USR-test-only",
        TestSellerId = 123,
        TestSellerConfirmed = true,
        PublicOrigin = "https://staging.example.com",
    };

    private static PaymentPreferenceRequest Request() => new(
        Guid.NewGuid(), PaymentEnvironment.Sandbox, Reference, 144.50m, "BRL", 18.50m,
        Now.AddMinutes(30), [new("sku-50", "Perfume — 50 ml", 2, 63m)]);

    private static MercadoPagoPaymentGateway Service(HttpClient client, MercadoPagoOptions? options = null, IClock? clock = null) =>
        new(client, Microsoft.Extensions.Options.Options.Create(options ?? Options()), clock ?? new MutableClock { UtcNow = Now });

    private static HttpResponseMessage Response(string url = CheckoutUrl, long sellerId = 123, string reference = Reference, string id = "123-preference") =>
        new(HttpStatusCode.Created)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id,
                collector_id = sellerId,
                external_reference = reference,
                init_point = url,
            }), Encoding.UTF8, "application/json"),
        };

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? Key { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Key = request.Headers.GetValues("X-Idempotency-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }
}
