using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class MercadoPagoOrderQueryTests
{
    private static readonly string[] InvalidTransactions = ["bad"];

    [Fact]
    public async Task ReadsFixedEndpointWithBearerAndReturnsOnlySanitizedObservation()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("https://api.mercadopago.com/v1/orders/ORD-query", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-only", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("X-Idempotency-Key"));
            Assert.Null(request.Content);
            return Task.FromResult(Response());
        });
        using var client = new HttpClient(handler);
        var result = await Service(client).GetOrderAsync(Request());
        Assert.Equal(PaymentOrderQueryStatus.Found, result.Status);
        Assert.Equal(ObservedOrderState.Created, result.Observation!.State);
        Assert.Equal(0, result.Observation.PaidAmount);
        Assert.Equal(TimeSpan.Zero, result.Observation.UpdatedAtUtc.Offset);
        Assert.False(result.Observation.HasTransactions);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cliente@", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("id", "ORD-other")]
    [InlineData("user_id", "999")]
    [InlineData("currency", "USD")]
    [InlineData("country_code", "ARG")]
    [InlineData("external_reference", "other-order")]
    [InlineData("total_amount", "70.01")]
    [InlineData("total_amount", "70,00")]
    [InlineData("total_paid_amount", "70.01")]
    [InlineData("total_paid_amount", "-1.00")]
    [InlineData("total_paid_amount", "0.001")]
    [InlineData("type", "point")]
    [InlineData("processing_mode", "automatic")]
    [InlineData("created_date", "2026-09-21")]
    [InlineData("created_date", "2026-09-21T18:00:00")]
    [InlineData("created_date", "2026-09-21T18:00:01Z")]
    [InlineData("last_updated_date", "2026-09-21T18:05:01Z")]
    [InlineData("last_updated_date", null)]
    [InlineData("status", "")]
    [InlineData("status", "<private>")]
    public async Task InvalidCanonicalResponseIsNotEvidence(string field, string? value)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(field, value)));
        using var client = new HttpClient(handler);
        var result = await Service(client).GetOrderAsync(Request());
        Assert.Equal(PaymentOrderQueryStatus.InvalidResponse, result.Status);
        Assert.Null(result.Observation);
    }

    [Theory]
    [InlineData("processed", ObservedOrderState.Processed)]
    [InlineData("action_required", ObservedOrderState.ActionRequired)]
    [InlineData("cancelled", ObservedOrderState.Other)]
    [InlineData("future_status", ObservedOrderState.Other)]
    public async Task DoesNotTranslateProviderStateIntoPaymentApproval(string value, ObservedOrderState expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("status", value)));
        using var client = new HttpClient(handler);
        var result = await Service(client).GetOrderAsync(Request());
        Assert.Equal(PaymentOrderQueryStatus.Found, result.Status);
        Assert.Equal(expected, result.Observation!.State);
    }

    [Theory]
    [InlineData("payments")]
    [InlineData("refunds")]
    [InlineData("chargebacks")]
    [InlineData("future_kind")]
    public async Task AnyTransactionIsFlaggedWithoutReturningItsPayload(string kind)
    {
        var body = Body();
        body["transactions"] = new Dictionary<string, object> { [kind] = new[] { new { id = "private-transaction", amount = "70.00" } } };
        using var handler = new Handler((_, _) => Task.FromResult(JsonResponse(body)));
        using var client = new HttpClient(handler);
        var result = await Service(client).GetOrderAsync(Request());
        Assert.Equal(PaymentOrderQueryStatus.Found, result.Status);
        Assert.True(result.Observation!.HasTransactions);
        Assert.DoesNotContain("private-transaction", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"id\":\"ORD-query\",\"id\":\"ORD-other\"}")]
    public async Task MalformedBodyIsRejected(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentOrderQueryStatus.InvalidResponse, (await Service(client).GetOrderAsync(Request())).Status);
    }

    [Fact]
    public async Task RejectsDuplicateFieldsAndMalformedTransactionsInOtherwiseValidBody()
    {
        string[] bodies =
        [
            JsonSerializer.Serialize(Body()).Replace("\"id\":\"ORD-query\"", "\"id\":\"ORD-query\",\"id\":\"ORD-query\"", StringComparison.Ordinal),
            JsonSerializer.Serialize(Body().Append(new KeyValuePair<string, object?>("transactions", new { payments = "bad" })).ToDictionary()),
            JsonSerializer.Serialize(Body().Append(new KeyValuePair<string, object?>("transactions", new { payments = InvalidTransactions })).ToDictionary()),
        ];
        foreach (var body in bodies)
        {
            using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            using var client = new HttpClient(handler);
            Assert.Equal(PaymentOrderQueryStatus.InvalidResponse, (await Service(client).GetOrderAsync(Request())).Status);
        }
    }

    [Theory]
    [InlineData(401, PaymentOrderQueryStatus.AuthenticationFailure)]
    [InlineData(403, PaymentOrderQueryStatus.AuthenticationFailure)]
    [InlineData(404, PaymentOrderQueryStatus.NotFound)]
    [InlineData(302, PaymentOrderQueryStatus.Unavailable)]
    [InlineData(429, PaymentOrderQueryStatus.Unavailable)]
    [InlineData(500, PaymentOrderQueryStatus.Unavailable)]
    public async Task FailureNeverRetriesOrCreatesOrder(int code, PaymentOrderQueryStatus expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("secret") }));
        using var client = new HttpClient(handler);
        var result = await Service(client).GetOrderAsync(Request());
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Observation);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InvalidInputsDisabledAndProductionNeverSend()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response()));
        using var client = new HttpClient(handler);
        var request = Request();
        PaymentOrderQuery[] invalid =
        [
            request with { ExternalOrderId = "https://evil.example" }, request with { ExternalOrderId = "ORD/../123" },
            request with { ExternalOrderId = "123-preference" }, request with { ExternalReference = "email@example.com" },
            request with { Environment = PaymentEnvironment.Production }, request with { Amount = 0 },
            request with { Amount = 70.001m }, request with { Currency = "USD" },
        ];
        foreach (var item in invalid)
        {
            Assert.Equal(PaymentOrderQueryStatus.InvalidRequest, (await Service(client).GetOrderAsync(item)).Status);
        }

        Assert.Equal(PaymentOrderQueryStatus.Disabled, (await Service(client, new MercadoPagoOptions()).GetOrderAsync(request)).Status);
        var options = PaymentDispatchTests.Configuration();
        options.Environment = PaymentEnvironment.Production;
        Assert.Equal(PaymentOrderQueryStatus.ConfigurationInvalid, (await Service(client, options).GetOrderAsync(request)).Status);
        Assert.Equal(0, handler.Calls);
        await using var app = new SallvatWebApplicationFactory();
        Assert.Equal(PaymentOrderQueryStatus.Disabled, (await app.Services.GetRequiredService<IPaymentGateway>().GetOrderAsync(request)).Status);
        Assert.NotNull(app.Services.GetRequiredService<IPaymentReconciliationService>());
    }

    [Fact]
    public async Task TransportTimeoutAndOversizedBodyAreUnavailable()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("secret"));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentOrderQueryStatus.Unavailable, (await Service(client).GetOrderAsync(Request())).Status);
        using var large = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 65_537)) }));
        using var largeClient = new HttpClient(large);
        Assert.Equal(PaymentOrderQueryStatus.Unavailable, (await Service(largeClient).GetOrderAsync(Request())).Status);
        using var slow = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        using var slowClient = new HttpClient(slow);
        var options = PaymentDispatchTests.Configuration();
        options.TimeoutSeconds = 2;
        Assert.Equal(PaymentOrderQueryStatus.Unavailable, (await Service(slowClient, options).GetOrderAsync(Request())).Status);
        Assert.Equal(1, slow.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response());
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(client).GetOrderAsync(Request(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(client).GetOrderAsync(Request(), cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static PaymentOrderQuery Request() => new("ORD-query", PaymentEnvironment.Sandbox, "SVT-query", 70m, "BRL");
    private static MercadoPagoPaymentGateway Service(HttpClient client, MercadoPagoOptions? options = null) =>
        new(client, Options.Create(options ?? PaymentDispatchTests.Configuration()), new PaymentDispatchTests.Clock());

    private static Dictionary<string, object?> Body() => new()
    {
        ["id"] = "ORD-query",
        ["type"] = "online",
        ["processing_mode"] = "manual",
        ["status"] = "created",
        ["user_id"] = "123",
        ["currency"] = "BRL",
        ["country_code"] = "BR",
        ["external_reference"] = "SVT-query",
        ["total_amount"] = "70.00",
        ["total_paid_amount"] = "0.00",
        ["created_date"] = "2026-09-21T18:00:00.000000000Z",
        ["last_updated_date"] = "2026-09-21T15:00:00-03:00",
        ["client_token"] = "secret",
        ["payer"] = new { email = "cliente@example.com" },
    };

    private static HttpResponseMessage Response(string? field = null, string? value = null)
    {
        var body = Body();
        if (field is not null)
        {
            body[field] = value;
        }

        return JsonResponse(body);
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return respond(request, cancellationToken);
        }
    }
}
