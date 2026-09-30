using System.Net;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;

namespace Sallvat.IntegrationTests.Payments;

public sealed class MercadoPagoRefundGatewayTests
{
    [Fact]
    public async Task TotalRefundUsesFixedEndpointEmptyBodyAndStableKey()
    {
        var request = new PaymentRefundSubmit(Guid.NewGuid(), "ORD-refund", PaymentEnvironment.Sandbox);
        using var handler = new Handler(async (message, token) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https://api.mercadopago.com/v1/orders/ORD-refund/refund", message.RequestUri!.AbsoluteUri);
            Assert.Equal(request.IdempotencyKey.ToString("D"), message.Headers.GetValues("X-Idempotency-Key").Single());
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Empty(await message.Content!.ReadAsByteArrayAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"ORD-refund\"}") };
        });
        using var client = new HttpClient(handler);
        var result = await Service(client).RefundOrderAsync(request);
        Assert.Equal(PaymentRefundSubmitResult.Accepted, result);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("production")]
    [InlineData("invalid-id")]
    [InlineData("empty-key")]
    public async Task InvalidConfigurationOrRequestNeverSends(string fault)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        var options = PaymentRefundFlowTests.Configuration();
        if (fault == "disabled") { options.RefundEnabled = false; }
        var result = await Service(client, options).RefundOrderAsync(new(fault == "empty-key" ? Guid.Empty : Guid.NewGuid(),
            fault == "invalid-id" ? "ORD/../other" : "ORD-refund", fault == "production" ? PaymentEnvironment.Production : PaymentEnvironment.Sandbox));
        Assert.Contains(result, new[] { PaymentRefundSubmitResult.Disabled, PaymentRefundSubmitResult.Invalid });
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(200, "{\"id\":\"ORD-other\"}")]
    [InlineData(200, "{\"id\":\"ORD-refund\",\"id\":\"ORD-refund\"}")]
    [InlineData(200, "not-json")]
    [InlineData(401, "{}")]
    [InlineData(409, "{}")]
    [InlineData(429, "{}")]
    [InlineData(500, "{}")]
    public async Task ErrorOrInvalidBodyIsUnknownAndNeverRetried(int code, string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) }));
        using var client = new HttpClient(handler);
        Assert.Equal(PaymentRefundSubmitResult.Unknown, await Service(client).RefundOrderAsync(new(Guid.NewGuid(), "ORD-refund", PaymentEnvironment.Sandbox)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancellationDuringSendRemainsUnknownButPreCancellationHasNoHttp()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler((_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); throw new InvalidOperationException(); });
        using var client = new HttpClient(handler);
        var request = new PaymentRefundSubmit(Guid.NewGuid(), "ORD-refund", PaymentEnvironment.Sandbox);
        Assert.Equal(PaymentRefundSubmitResult.Unknown, await Service(client).RefundOrderAsync(request, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(client).RefundOrderAsync(request, cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static MercadoPagoPaymentGateway Service(HttpClient client, MercadoPagoOptions? options = null) =>
        new(client, Options.Create(options ?? PaymentRefundFlowTests.Configuration()), new PaymentDispatchTests.Clock());
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return respond(request, cancellationToken); }
    }
}
