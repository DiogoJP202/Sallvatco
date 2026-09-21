using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Payments;

internal sealed class MercadoPagoPaymentGateway(
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> configuredOptions,
    IClock clock) : IPaymentGateway
{
    private static readonly Uri PreferenceEndpoint = new("https://api.mercadopago.com/checkout/preferences");

    public async Task<PaymentPreferenceResult> CreatePreferenceAsync(
        PaymentPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.Enabled)
        {
            return new(PaymentPreferenceStatus.Disabled);
        }

        if (!MercadoPagoOptions.IsValid(options))
        {
            return new(PaymentPreferenceStatus.ConfigurationInvalid);
        }

        var now = clock.UtcNow;
        if (!IsValid(request, now))
        {
            return new(PaymentPreferenceStatus.InvalidRequest);
        }

        var origin = new Uri(options.PublicOrigin, UriKind.Absolute);
        using var message = new HttpRequestMessage(HttpMethod.Post, PreferenceEndpoint)
        {
            Content = JsonContent.Create(new
            {
                items = request.Items.Select(item => new
                {
                    id = item.Id,
                    title = item.Title,
                    quantity = item.Quantity,
                    currency_id = request.Currency,
                    unit_price = item.UnitAmount,
                }).ToArray(),
                shipments = new { cost = request.ShippingTotal, mode = "not_specified" },
                external_reference = request.ExternalReference,
                back_urls = new
                {
                    success = new Uri(origin, "pagamentos/retorno/sucesso").AbsoluteUri,
                    pending = new Uri(origin, "pagamentos/retorno/pendente").AbsoluteUri,
                    failure = new Uri(origin, "pagamentos/retorno/falha").AbsoluteUri,
                },
                auto_return = "approved",
                expires = true,
                expiration_date_from = now,
                expiration_date_to = request.ExpiresAtUtc,
            }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey.ToString("D"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        // Once sending starts, cancellation/transport failure cannot prove absence of a preference.
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new(PaymentPreferenceStatus.AuthenticationFailure);
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                return new(PaymentPreferenceStatus.Rejected);
            }

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new(PaymentPreferenceStatus.OutcomeUnknown);
            }

            await response.Content.LoadIntoBufferAsync(65_536, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            return ParseResponse(document.RootElement, request, options);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Do not log provider bodies, credentials or exception messages; never retry a POST here.
            return new(PaymentPreferenceStatus.OutcomeUnknown);
        }
    }

    private PaymentPreferenceResult ParseResponse(
        JsonElement root,
        PaymentPreferenceRequest request,
        MercadoPagoOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("collector_id", out var collector)
            || collector.ValueKind != JsonValueKind.Number
            || !collector.TryGetInt64(out var collectorId) || collectorId != options.TestSellerId
            || Text(root, "external_reference") != request.ExternalReference
            || Text(root, "id") is not { } id || !IsIdentifier(id, Payment.PreferenceIdMaxLength)
            || !Uri.TryCreate(Text(root, "init_point"), UriKind.Absolute, out var checkout)
            || checkout.Scheme != Uri.UriSchemeHttps || !checkout.IsDefaultPort
            || checkout.Host != "www.mercadopago.com.br"
            || !checkout.AbsolutePath.StartsWith("/checkout/", StringComparison.Ordinal)
            || checkout.Query != "?pref_id=" + Uri.EscapeDataString(id)
            || !string.IsNullOrEmpty(checkout.UserInfo) || !string.IsNullOrEmpty(checkout.Fragment)
            || clock.UtcNow >= request.ExpiresAtUtc)
        {
            return new(PaymentPreferenceStatus.OutcomeUnknown);
        }

        return new(PaymentPreferenceStatus.Created, id, checkout);
    }

    private static bool IsValid(PaymentPreferenceRequest request, DateTimeOffset now)
    {
        if (request.Environment != PaymentEnvironment.Sandbox
            || request.IdempotencyKey == Guid.Empty
            || !IsIdentifier(request.ExternalReference, Order.OrderNumberMaxLength)
            || request.Currency != "BRL" || !IsMoney(request.Amount) || request.Amount <= 0
            || !IsMoney(request.ShippingTotal)
            || request.ExpiresAtUtc.Offset != TimeSpan.Zero || request.ExpiresAtUtc <= now
            || request.Items is null || request.Items.Count is < 1 or > 100)
        {
            return false;
        }

        var total = request.ShippingTotal;
        foreach (var item in request.Items)
        {
            if (item is null || !IsIdentifier(item.Id, 100)
                || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 256
                || item.Title.Any(char.IsControl) || item.Quantity is < 1 or > 1_000
                || !IsMoney(item.UnitAmount) || item.UnitAmount <= 0)
            {
                return false;
            }

            total += item.Quantity * item.UnitAmount;
        }

        return total == request.Amount;
    }

    private static bool IsMoney(decimal amount) =>
        amount is >= 0 and <= 9_999_999_999_999_999.99m && decimal.Round(amount, 2) == amount;

    private static bool IsIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;
}
