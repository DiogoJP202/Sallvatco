using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentWebhookService(
    SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> configuredOptions, IClock clock) : IPaymentWebhookService
{
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);

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

            return await ApplyAsync(snapshot.Id, request.DataId, deliveryKey, response, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException || PaymentValidation.IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return PaymentWebhookResult.Retry;
        }
    }

    private async Task<PaymentWebhookResult> ApplyAsync(long paymentId, string externalId, string key,
        PaymentOrderQueryResult response, CancellationToken cancellationToken)
    {
        var relational = db.Database.IsRelational();
        if (!relational)
        {
            await InMemoryLock.WaitAsync(cancellationToken);
        }

        try
        {
            await using var transaction = relational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            db.ChangeTracker.Clear();
            if (await db.WebhookEvents.AnyAsync(e => e.DeliveryKey == key, cancellationToken))
            {
                return PaymentWebhookResult.Accepted;
            }

            var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
            var order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            var now = clock.UtcNow;
            var outcome = WebhookOutcome.Observed;
            var observation = response.Observation;
            if (response.Status != PaymentOrderQueryStatus.Found || observation is null
                || observation.ExternalOrderId != externalId || payment.ExternalOrderId != externalId
                || payment.Amount != order.GrandTotal || payment.Currency != order.Currency || payment.ExternalReference != order.OrderNumber
                || payment.DispatchStartedAtUtc is not { } started || observation.CreatedAtUtc < started.AddMinutes(-5))
            {
                Review(payment, order, PaymentAttentionReason.CanonicalMismatch, now);
                outcome = WebhookOutcome.RequiresAttention;
            }
            else if (payment.ProviderUpdatedAtUtc is { } last && observation.UpdatedAtUtc < last)
            {
                // A concurrent or delayed GET must not roll back a confirmed capture.
                outcome = WebhookOutcome.Observed;
            }
            else if (observation.SettledPaymentId is { } externalPaymentId && observation.State == ObservedOrderState.Processed
                && observation.PaidAmount == payment.Amount)
            {
                if (payment.ExternalPaymentId == externalPaymentId)
                {
                    outcome = WebhookOutcome.Observed;
                }
                else if (payment.ExternalPaymentId is not null)
                {
                    Review(payment, order, PaymentAttentionReason.FinancialReview, now);
                    outcome = WebhookOutcome.RequiresAttention;
                }
                else
                {
                    outcome = await ConfirmAsync(payment, order, externalPaymentId, observation.UpdatedAtUtc, now, cancellationToken);
                }
            }
            else if (observation.State != ObservedOrderState.Created || observation.HasTransactions || observation.PaidAmount != 0)
            {
                Review(payment, order, PaymentAttentionReason.FinancialReview, now);
                outcome = WebhookOutcome.RequiresAttention;
            }

            db.WebhookEvents.Add(new WebhookEvent(key, payment.Id, externalId, outcome, now));
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return PaymentWebhookResult.Accepted;
        }
        finally
        {
            if (!relational)
            {
                InMemoryLock.Release();
            }
        }
    }

    private async Task<WebhookOutcome> ConfirmAsync(Payment payment, Order order, string transactionId,
        DateTimeOffset providerUpdated, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Payments.AnyAsync(p => p.Id != payment.Id && p.Provider == payment.Provider
            && p.Environment == payment.Environment && p.ExternalPaymentId == transactionId, cancellationToken))
        {
            Review(payment, order, PaymentAttentionReason.FinancialReview, now);
            return WebhookOutcome.RequiresAttention;
        }

        var items = await db.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync(cancellationToken);
        var reservations = await db.StockReservations.Where(r => r.OrderId == order.Id).OrderBy(r => r.ProductVariantId).ToListAsync(cancellationToken);
        var ids = reservations.Select(r => r.ProductVariantId).ToArray();
        var variants = await db.ProductVariants.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        if (payment.Status != PaymentStatus.Pending || payment.DispatchState != PaymentDispatchState.Completed
            || order.Status != OrderStatus.PendingPayment || now >= payment.ExpiresAtUtc || payment.ExpiresAtUtc != order.ExpiresAtUtc
            || !PaymentValidation.HasValidSnapshots(order, items, reservations, now)
            || reservations.Any(r => !variants.TryGetValue(r.ProductVariantId, out var v) || v.Reserved < r.Quantity || v.OnHand < r.Quantity))
        {
            Review(payment, order, PaymentAttentionReason.LateApproval, now);
            return WebhookOutcome.RequiresAttention;
        }

        payment.ConfirmOrderPayment(transactionId, providerUpdated, now);
        order.TransitionTo(OrderStatus.Paid, now);
        foreach (var reservation in reservations)
        {
            var variant = variants[reservation.ProductVariantId];
            reservation.Consume(now);
            variant.ConsumeReservation(reservation.Quantity, now);
            db.InventoryMovements.Add(new InventoryMovement(variant.Id, InventoryMovementType.Sale, -reservation.Quantity,
                variant.OnHand, variant.Reserved, null, $"Pagamento confirmado do pedido {order.OrderNumber}", now));
        }

        return WebhookOutcome.Confirmed;
    }

    private static void Review(Payment payment, Order order, PaymentAttentionReason reason, DateTimeOffset now)
    {
        payment.RequireCanonicalReview(reason, now);
        if (Order.CanTransition(order.Status, OrderStatus.RequiresAttention))
        {
            order.TransitionTo(OrderStatus.RequiresAttention, now, "Divergência de pagamento: " + reason);
        }
        // Cancelled/Delivered orders retain their fulfillment state; the durable payment receipt records review.
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
