using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Payments;

internal sealed partial class MercadoPagoPaymentGateway
{
    public async Task<PaymentOrderQueryResult> GetOrderAsync(PaymentOrderQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.OrdersEnabled)
        {
            return new(PaymentOrderQueryStatus.Disabled);
        }

        if (!MercadoPagoOptions.IsValid(options))
        {
            return new(PaymentOrderQueryStatus.ConfigurationInvalid);
        }

        if (request.Environment != PaymentEnvironment.Sandbox
            || !IsIdentifier(request.ExternalOrderId, 64) || !request.ExternalOrderId.StartsWith("ORD", StringComparison.Ordinal)
            || !IsIdentifier(request.ExternalReference, 64) || request.Currency != "BRL" || !IsMoney(request.Amount) || request.Amount <= 0)
        {
            return new(PaymentOrderQueryStatus.InvalidRequest);
        }

        // Fixed host and validated identifier: never fetch a URL provided by a notification or browser.
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(OrderEndpoint.AbsoluteUri + "/" + request.ExternalOrderId));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new(PaymentOrderQueryStatus.AuthenticationFailure);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(PaymentOrderQueryStatus.NotFound);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new(PaymentOrderQueryStatus.Unavailable);
            }

            await response.Content.LoadIntoBufferAsync(65_536, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            return ParseOrderObservation(document.RootElement, request, options);
        }
        catch (JsonException)
        {
            return new(PaymentOrderQueryStatus.InvalidResponse);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Unlike POST dispatch, cancelling this read cannot abandon a financial write.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return new(PaymentOrderQueryStatus.Unavailable);
        }
    }

    private PaymentOrderQueryResult ParseOrderObservation(JsonElement root, PaymentOrderQuery request, MercadoPagoOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root)
            || Text(root, "id") != request.ExternalOrderId
            || Text(root, "type") != "online" || Text(root, "processing_mode") != "manual"
            || Text(root, "user_id") != options.TestSellerId.ToString(CultureInfo.InvariantCulture)
            || Text(root, "external_reference") != request.ExternalReference
            || Text(root, "currency") != request.Currency || Text(root, "country_code") is not ("BR" or "BRA")
            || !TryMoney(Text(root, "total_amount"), out var amount) || amount != request.Amount
            || !TryMoney(Text(root, "total_paid_amount"), out var paid) || paid > amount
            || !TryProviderTimestamp(Text(root, "created_date"), out var created)
            || !TryProviderTimestamp(Text(root, "last_updated_date"), out var updated)
            || updated < created || updated > clock.UtcNow.AddMinutes(5)
            || Text(root, "status") is not { Length: > 0 and <= 64 } state
            || !state.All(character => char.IsAsciiLetterLower(character) || character == '_')
            || !TryTransactions(root, out var hasTransactions))
        {
            return new(PaymentOrderQueryStatus.InvalidResponse);
        }

        var observedState = state switch
        {
            "created" => ObservedOrderState.Created,
            "processed" => ObservedOrderState.Processed,
            "action_required" => ObservedOrderState.ActionRequired,
            _ => ObservedOrderState.Other,
        };
        return new(PaymentOrderQueryStatus.Found, new(request.ExternalOrderId, observedState, paid, created, updated, hasTransactions));
    }

    private static bool TryTransactions(JsonElement root, out bool hasTransactions)
    {
        hasTransactions = false;
        if (!root.TryGetProperty("transactions", out var transactions))
        {
            return true;
        }

        if (transactions.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Any entry, including refunds, chargebacks and future transaction kinds, requires review.
        foreach (var property in transactions.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 100
                || property.Value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
            {
                return false;
            }

            hasTransactions |= property.Value.GetArrayLength() != 0;
        }

        return true;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return element.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicateProperties(property.Value));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(HasDuplicateProperties);
    }

    private static bool TryProviderTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is null || value.Length > 40 || !ProviderTimestampPattern().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        timestamp = parsed.ToUniversalTime();
        return true;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderTimestampPattern();
}
