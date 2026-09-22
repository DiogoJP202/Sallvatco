using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Payments;

internal sealed partial class MercadoPagoPaymentGateway
{
    private static readonly Uri OrderEndpoint = new("https://api.mercadopago.com/v1/orders");

    public async Task<PaymentOrderResult> CreateOrderAsync(
        PaymentOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.OrdersEnabled)
        {
            return new(PaymentOrderStatus.Disabled);
        }

        if (!MercadoPagoOptions.IsValid(options))
        {
            return new(PaymentOrderStatus.ConfigurationInvalid);
        }

        var now = clock.UtcNow;
        if (!IsValidOrder(request, now))
        {
            return new(PaymentOrderStatus.InvalidRequest);
        }

        var origin = new Uri(options.PublicOrigin, UriKind.Absolute);
        // Round down; never extend the local reservation to accommodate provider expiration.
        var duration = TimeSpan.FromSeconds(Math.Floor((request.ExpiresAtUtc - now).TotalSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, OrderEndpoint)
        {
            Content = JsonContent.Create(new
            {
                type = "online",
                processing_mode = "manual",
                total_amount = MoneyText(request.Amount),
                external_reference = request.ExternalReference,
                expiration_time = XmlConvert.ToString(duration),
                items = request.Items.Select(item => new
                {
                    external_code = item.Id,
                    title = item.Title,
                    quantity = item.Quantity,
                    unit_price = MoneyText(item.UnitAmount),
                }).ToArray(),
                config = new
                {
                    online = new
                    {
                        success_url = new Uri(origin, "pagamentos/retorno/sucesso").AbsoluteUri,
                        pending_url = new Uri(origin, "pagamentos/retorno/pendente").AbsoluteUri,
                        failure_url = new Uri(origin, "pagamentos/retorno/falha").AbsoluteUri,
                        auto_return = "approved",
                    },
                },
            }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey.ToString("D"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new(PaymentOrderStatus.AuthenticationFailure);
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                return new(PaymentOrderStatus.Rejected);
            }

            // Conflict/locked/timeout may already represent an external order. Never generate a new key.
            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new(PaymentOrderStatus.OutcomeUnknown);
            }

            await response.Content.LoadIntoBufferAsync(65_536, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            return ParseOrderResponse(document.RootElement, request, options);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Do not expose bodies, client_token, credentials or transport details; no automatic POST retry.
            return new(PaymentOrderStatus.OutcomeUnknown);
        }
    }

    private PaymentOrderResult ParseOrderResponse(JsonElement root, PaymentOrderRequest request, MercadoPagoOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object
            || Text(root, "type") != "online" || Text(root, "processing_mode") != "manual"
            || Text(root, "status") != "created"
            || Text(root, "user_id") != options.TestSellerId.ToString(CultureInfo.InvariantCulture)
            || Text(root, "currency") != request.Currency || Text(root, "country_code") is not ("BR" or "BRA")
            || Text(root, "external_reference") != request.ExternalReference
            || !TryMoney(Text(root, "total_amount"), out var amount) || amount != request.Amount
            || !TryMoney(Text(root, "total_paid_amount"), out var paid) || paid != 0
            || Text(root, "id") is not { } id || !IsIdentifier(id, 64)
            || !id.StartsWith("ORD", StringComparison.Ordinal)
            || !Uri.TryCreate(Text(root, "checkout_url"), UriKind.Absolute, out var checkout)
            || checkout.Scheme != Uri.UriSchemeHttps || !checkout.IsDefaultPort
            || checkout.Host != "www.mercadopago.com.br" || checkout.AbsolutePath != "/checkout/v1/redirect"
            || checkout.Query != "?order_id=" + Uri.EscapeDataString(id)
            || !string.IsNullOrEmpty(checkout.UserInfo) || !string.IsNullOrEmpty(checkout.Fragment)
            || clock.UtcNow >= request.ExpiresAtUtc)
        {
            return new(PaymentOrderStatus.OutcomeUnknown);
        }

        return new(PaymentOrderStatus.Created, id, checkout);
    }

    private static bool IsValidOrder(PaymentOrderRequest request, DateTimeOffset now)
    {
        if (request.Environment != PaymentEnvironment.Sandbox || request.IdempotencyKey == Guid.Empty
            || !IsIdentifier(request.ExternalReference, 64) || request.Currency != "BRL"
            || !IsMoney(request.Amount) || request.Amount <= 0
            || request.ExpiresAtUtc.Offset != TimeSpan.Zero
            || request.ExpiresAtUtc - now < TimeSpan.FromSeconds(1)
            || request.ExpiresAtUtc - now > TimeSpan.FromDays(1)
            || request.Items is null || request.Items.Count is < 1 or > 100)
        {
            return false;
        }

        decimal total = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            if (item is null || !IsIdentifier(item.Id, 100) || !ids.Add(item.Id)
                || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 256 || item.Title.Any(char.IsControl)
                || item.Quantity is < 1 or > 1_000 || !IsMoney(item.UnitAmount) || item.UnitAmount <= 0)
            {
                return false;
            }

            total += item.UnitAmount * item.Quantity;
        }

        return total == request.Amount;
    }

    private static string MoneyText(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryMoney(string? text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && IsMoney(amount);
}
