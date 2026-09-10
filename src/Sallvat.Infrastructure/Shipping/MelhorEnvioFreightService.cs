using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sallvat.Application.Shipping;
using Sallvat.Application.Time;

namespace Sallvat.Infrastructure.Shipping;

internal sealed partial class MelhorEnvioFreightService(
    HttpClient httpClient,
    IOptions<MelhorEnvioOptions> configuredOptions,
    IMemoryCache cache,
    IClock clock,
    ILogger<MelhorEnvioFreightService> logger) : IFreightService
{
    private const string QuotePath = "api/v2/me/shipment/calculate";
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<FreightQuoteResult> QuoteAsync(
        FreightQuoteRequest request,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = configuredOptions.Value;
        if (!options.Enabled)
        {
            return Failure(
                FreightQuoteStatus.ConfigurationMissing,
                "A cotação de frete ainda não está configurada neste ambiente.");
        }

        var destination = MelhorEnvioOptions.NormalizePostalCode(
            request.DestinationPostalCode);
        var origin = MelhorEnvioOptions.NormalizePostalCode(
            options.OriginPostalCode);
        if (destination is null
            || origin is null
            || !IsValid(request.Items))
        {
            return Failure(
                FreightQuoteStatus.Invalid,
                "Não foi possível montar a cotação com o CEP e os itens informados.");
        }

        var cacheKey = CacheKey(options, destination, request.Items);
        if (!forceRefresh
            && cache.TryGetValue(cacheKey, out FreightQuoteResult? cached)
            && cached is not null)
        {
            return cached;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, QuotePath)
        {
            Content = JsonContent.Create(
                ToPayload(options, origin, destination, request.Items),
                options: SerializerOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            options.AccessToken.Trim());

        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var failure = FromStatusCode(response.StatusCode);
            if (failure is not null)
            {
                LogFailure(logger, response.StatusCode);
                return failure;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            var quote = Parse(document.RootElement, options);
            if (!quote.Succeeded)
            {
                LogNoOptions(logger);
                return quote;
            }

            cache.Set(
                cacheKey,
                quote,
                TimeSpan.FromSeconds(options.CacheSeconds));
            LogSuccess(logger, quote.Options.Count);
            return quote;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger);
            return Failure(
                FreightQuoteStatus.Unavailable,
                "A cotação demorou mais que o esperado. Tente novamente.");
        }
        catch (HttpRequestException exception)
        {
            LogTransportFailure(logger, exception);
            return Failure(
                FreightQuoteStatus.Unavailable,
                "O serviço de frete está temporariamente indisponível.");
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, exception);
            return Failure(
                FreightQuoteStatus.Unavailable,
                "O serviço de frete retornou uma resposta inválida.");
        }
    }

    private FreightQuoteResult Parse(
        JsonElement root,
        MelhorEnvioOptions options)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return Failure(
                FreightQuoteStatus.Unavailable,
                "O serviço de frete retornou uma resposta inválida.");
        }

        var quotedAt = clock.UtcNow;
        var expiresAt = quotedAt.AddSeconds(options.QuoteValiditySeconds);
        var quotes = new List<FreightQuoteOption>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || HasProviderError(item)
                || !TryText(item, "id", out var id)
                || !TryText(item, "name", out var service)
                || !TryNestedText(item, "company", "name", out var carrier)
                || !TryMoney(item, "custom_price", out var price)
                || price <= 0
                || !TryDeliveryRange(item, out var minimum, out var maximum))
            {
                continue;
            }

            quotes.Add(new FreightQuoteOption(
                $"melhor-envio:{id}",
                carrier,
                service,
                decimal.Round(price, 2, MidpointRounding.AwayFromZero),
                "BRL",
                minimum,
                maximum,
                quotedAt,
                expiresAt));
        }

        var ordered = quotes
            .OrderBy(item => item.Price)
            .ThenBy(item => item.MaximumBusinessDays)
            .ThenBy(item => item.Carrier, StringComparer.Ordinal)
            .ToArray();
        return ordered.Length > 0
            ? FreightQuoteResult.Success(ordered)
            : Failure(
                FreightQuoteStatus.Unavailable,
                "Nenhuma opção de entrega está disponível para este CEP.");
    }

    private static bool IsValid(IReadOnlyList<FreightQuoteItem>? items) =>
        items is { Count: > 0 and <= 100 }
        && items.All(item =>
            !string.IsNullOrWhiteSpace(item.Reference)
            && item.Reference.Length <= 100
            && item.Quantity is > 0 and <= 100
            && item.UnitPrice > 0
            && item.Currency.Equals("BRL", StringComparison.OrdinalIgnoreCase)
            && item.WeightKg > 0
            && item.HeightCm > 0
            && item.WidthCm > 0
            && item.LengthCm > 0);

    private static MelhorEnvioQuotePayload ToPayload(
        MelhorEnvioOptions options,
        string origin,
        string destination,
        IReadOnlyList<FreightQuoteItem> items) =>
        new(
            new(origin),
            new(destination),
            items
                .OrderBy(item => item.Reference, StringComparer.Ordinal)
                .Select(item => new MelhorEnvioProduct(
                    item.Reference,
                    item.WidthCm,
                    item.HeightCm,
                    item.LengthCm,
                    item.WeightKg,
                    item.UnitPrice,
                    item.Quantity))
                .ToArray(),
            new(false, false),
            string.IsNullOrWhiteSpace(options.ServiceIds)
                ? null
                : options.ServiceIds.Trim());

    private static string CacheKey(
        MelhorEnvioOptions options,
        string destination,
        IReadOnlyList<FreightQuoteItem> items)
    {
        var builder = new StringBuilder()
            .Append(MelhorEnvioOptions.NormalizePostalCode(
                options.OriginPostalCode))
            .Append('|')
            .Append(destination)
            .Append('|')
            .Append(options.ServiceIds?.Trim());
        foreach (var item in items.OrderBy(
            item => item.Reference,
            StringComparer.Ordinal))
        {
            builder
                .Append('|').Append(item.Reference)
                .Append(':').Append(item.Quantity)
                .Append(':').Append(item.UnitPrice.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':').Append(item.WeightKg.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':').Append(item.HeightCm.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':').Append(item.WidthCm.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':').Append(item.LengthCm.ToString(
                    CultureInfo.InvariantCulture));
        }

        return $"freight:{Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString())))}";
    }

    private static FreightQuoteResult? FromStatusCode(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Failure(
                FreightQuoteStatus.AuthenticationFailure,
                "A integração de frete precisa ser reautorizada."),
            HttpStatusCode.TooManyRequests => Failure(
                FreightQuoteStatus.RateLimited,
                "O serviço de frete está ocupado. Aguarde e tente novamente."),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest =>
                Failure(
                    FreightQuoteStatus.Invalid,
                    "O CEP ou as características do volume não foram aceitos pelo serviço de frete."),
            _ => Failure(
                FreightQuoteStatus.Unavailable,
                "O serviço de frete está temporariamente indisponível."),
        };

    private static bool HasProviderError(JsonElement item) =>
        item.TryGetProperty("error", out var value)
        && value.ValueKind != JsonValueKind.Null
        && (!string.IsNullOrWhiteSpace(value.ToString()));

    private static bool TryNestedText(
        JsonElement item,
        string containerName,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        return item.TryGetProperty(containerName, out var container)
            && container.ValueKind == JsonValueKind.Object
            && TryText(container, propertyName, out value);
    }

    private static bool TryText(
        JsonElement item,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null
                or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : property.ToString().Trim();
        return value.Length > 0;
    }

    private static bool TryMoney(
        JsonElement item,
        string propertyName,
        out decimal value)
    {
        value = 0;
        return item.TryGetProperty(propertyName, out var property)
            && (property.ValueKind == JsonValueKind.Number
                ? property.TryGetDecimal(out value)
                : property.ValueKind == JsonValueKind.String
                    && decimal.TryParse(
                        property.GetString(),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out value));
    }

    private static bool TryDeliveryRange(
        JsonElement item,
        out int minimum,
        out int maximum)
    {
        minimum = 0;
        maximum = 0;
        if (item.TryGetProperty("custom_delivery_range", out var range)
            && range.ValueKind == JsonValueKind.Object
            && TryInteger(range, "min", out minimum)
            && TryInteger(range, "max", out maximum)
            && minimum > 0
            && maximum >= minimum)
        {
            return true;
        }

        return TryInteger(item, "custom_delivery_time", out minimum)
            && minimum > 0
            && (maximum = minimum) > 0;
    }

    private static bool TryInteger(
        JsonElement item,
        string propertyName,
        out int value)
    {
        value = 0;
        return item.TryGetProperty(propertyName, out var property)
            && (property.ValueKind == JsonValueKind.Number
                ? property.TryGetInt32(out value)
                : property.ValueKind == JsonValueKind.String
                    && int.TryParse(
                        property.GetString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out value));
    }

    private static FreightQuoteResult Failure(
        FreightQuoteStatus status,
        string message) => FreightQuoteResult.Failure(status, message);

    [LoggerMessage(
        EventId = 4301,
        Level = LogLevel.Information,
        Message = "Melhor Envio returned {QuoteCount} valid freight options.")]
    private static partial void LogSuccess(ILogger logger, int quoteCount);

    [LoggerMessage(
        EventId = 4302,
        Level = LogLevel.Warning,
        Message = "Melhor Envio quote failed with HTTP {StatusCode}.")]
    private static partial void LogFailure(
        ILogger logger,
        HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 4303,
        Level = LogLevel.Warning,
        Message = "Melhor Envio returned no valid freight options.")]
    private static partial void LogNoOptions(ILogger logger);

    [LoggerMessage(
        EventId = 4304,
        Level = LogLevel.Warning,
        Message = "Melhor Envio quote timed out.")]
    private static partial void LogTimeout(ILogger logger);

    [LoggerMessage(
        EventId = 4305,
        Level = LogLevel.Warning,
        Message = "Melhor Envio quote could not reach the provider.")]
    private static partial void LogTransportFailure(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 4306,
        Level = LogLevel.Error,
        Message = "Melhor Envio returned an invalid quote response.")]
    private static partial void LogInvalidResponse(
        ILogger logger,
        Exception exception);

    private sealed record MelhorEnvioQuotePayload(
        MelhorEnvioPostalCode From,
        MelhorEnvioPostalCode To,
        IReadOnlyList<MelhorEnvioProduct> Products,
        MelhorEnvioAdditionalOptions Options,
        string? Services);

    private sealed record MelhorEnvioPostalCode(
        [property: JsonPropertyName("postal_code")] string PostalCode);

    private sealed record MelhorEnvioProduct(
        string Id,
        decimal Width,
        decimal Height,
        decimal Length,
        decimal Weight,
        [property: JsonPropertyName("insurance_value")] decimal InsuranceValue,
        int Quantity);

    private sealed record MelhorEnvioAdditionalOptions(
        bool Receipt,
        [property: JsonPropertyName("own_hand")] bool OwnHand);
}
