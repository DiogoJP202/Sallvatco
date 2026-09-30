using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Payments;

internal sealed partial class MercadoPagoPaymentGateway
{
    public async Task<PaymentRefundSubmitResult> RefundOrderAsync(PaymentRefundSubmit request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.RefundEnabled || !MercadoPagoOptions.IsValid(options)) { return PaymentRefundSubmitResult.Disabled; }
        if (request.IdempotencyKey == Guid.Empty || request.Environment != PaymentEnvironment.Sandbox
            || !IsIdentifier(request.ExternalOrderId, 64) || !request.ExternalOrderId.StartsWith("ORD", StringComparison.Ordinal))
        { return PaymentRefundSubmitResult.Invalid; }
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(OrderEndpoint.AbsoluteUri + "/" + request.ExternalOrderId + "/refund"))
        {
            // Empty body means total refund. Never accept a browser-supplied amount or payment ID.
            Content = new ByteArrayContent([]),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey.ToString("D"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK) { return PaymentRefundSubmitResult.Unknown; }
            await response.Content.LoadIntoBufferAsync(65_536, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            // Even a valid 200 is not canonical proof: a separate GET must confirm the refund.
            return json.RootElement.ValueKind == JsonValueKind.Object && !HasDuplicateProperties(json.RootElement)
                && Text(json.RootElement, "id") == request.ExternalOrderId ? PaymentRefundSubmitResult.Accepted : PaymentRefundSubmitResult.Unknown;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            // Once SendAsync starts, cancellation can hide an accepted financial write. Never authorize re-POST.
            return PaymentRefundSubmitResult.Unknown;
        }
    }

    private static ConfirmedOrderRefund? TotalRefund(JsonElement root, decimal amount)
    {
        if (Text(root, "status") is not ("processed" or "refunded") || Text(root, "status_detail") != "refunded"
            || !root.TryGetProperty("transactions", out var transactions) || transactions.ValueKind != JsonValueKind.Object
            || !transactions.TryGetProperty("payments", out var payments) || payments.ValueKind != JsonValueKind.Array || payments.GetArrayLength() != 1
            || !transactions.TryGetProperty("refunds", out var refunds) || refunds.ValueKind != JsonValueKind.Array || refunds.GetArrayLength() != 1
            || transactions.EnumerateObject().Any(p => p.Name is not ("payments" or "refunds") && p.Value.GetArrayLength() != 0))
        { return null; }
        var payment = payments[0];
        var refund = refunds[0];
        return Text(payment, "id") is { Length: >= 4 } paymentId && IsIdentifier(paymentId, 64) && paymentId.StartsWith("PAY", StringComparison.Ordinal)
            && Text(payment, "status") == "refunded" && Text(payment, "status_detail") == "refunded"
            && TryMoney(Text(payment, "amount"), out var capture) && capture == amount
            && Text(refund, "transaction_id") == paymentId && Text(refund, "status") == "processed"
            && TryMoney(Text(refund, "amount"), out var returned) && returned == amount
            && Text(refund, "id") is { Length: >= 4 } refundId && IsIdentifier(refundId, 64) && refundId.StartsWith("REF", StringComparison.Ordinal)
                ? new(refundId, paymentId, returned) : null;
    }
}
