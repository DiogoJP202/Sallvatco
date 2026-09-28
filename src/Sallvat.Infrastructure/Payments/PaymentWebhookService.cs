using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentWebhookService(
    SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> configuredOptions, IClock clock) : IPaymentWebhookService
{
    public async Task<PaymentWebhookResult> HandleAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.WebhookEnabled || !MercadoPagoOptions.IsValid(options))
        {
            return PaymentWebhookResult.Disabled;
        }

        if (request.Body is null || request.Body.Length is 0 or > 16_384 || request.DataId is null
            || request.Signature is null || request.RequestId is null)
        {
            return PaymentWebhookResult.Invalid;
        }

        var deliveryKey = MercadoPagoWebhookSignature.Verify(request, options.WebhookSecret, clock.UtcNow);
        if (deliveryKey is null)
        {
            return PaymentWebhookResult.Unauthorized;
        }

        if (!ValidEnvelope(request))
        {
            return PaymentWebhookResult.Invalid;
        }

        if (db.ChangeTracker.HasChanges())
        {
            return PaymentWebhookResult.Retry;
        }

        try
        {
            if (await db.WebhookEvents.AnyAsync(e => e.DeliveryKey == deliveryKey, cancellationToken))
            {
                return PaymentWebhookResult.Accepted;
            }

            var snapshot = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Provider == "MercadoPago"
                && p.Environment == PaymentEnvironment.Sandbox && p.ExternalOrderId == request.DataId, cancellationToken);
            if (snapshot is null)
            {
                // Notification can arrive before dispatch saves its ID. Do not bind using unsigned body/reference.
                return PaymentWebhookResult.Retry;
            }

            var response = await gateway.GetOrderAsync(new(snapshot.ExternalOrderId!, snapshot.Environment,
                snapshot.ExternalReference, snapshot.Amount, snapshot.Currency), cancellationToken);
            if (response.Status is not (PaymentOrderQueryStatus.Found or PaymentOrderQueryStatus.InvalidResponse))
            {
                return PaymentWebhookResult.Retry;
            }

            await new PaymentObservationProcessor(db, clock).ApplyAsync(snapshot.Id, request.DataId, deliveryKey, response, null, cancellationToken);
            return PaymentWebhookResult.Accepted;
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException || PaymentValidation.IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return PaymentWebhookResult.Retry;
        }
    }

    private static bool ValidEnvelope(PaymentWebhookRequest request)
    {
        try
        {
            using var json = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object && !DuplicateProperties(root)
                && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "order"
                && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == request.DataId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool DuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return element.EnumerateObject().Any(p => !names.Add(p.Name) || DuplicateProperties(p.Value));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(DuplicateProperties);
    }
}
