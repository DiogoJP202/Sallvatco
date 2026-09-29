using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

// One financial transaction boundary for signed notifications and authorized recovery queries.
internal sealed class PaymentObservationProcessor(SallvatDbContext db, IClock clock)
{
    internal static readonly SemaphoreSlim InMemoryLock = new(1, 1);

    internal async Task<PaymentRecoveryResult> ApplyAsync(long paymentId, string externalId, string? deliveryKey,
        PaymentOrderQueryResult response, RecoveryEvidence? recovery, CancellationToken cancellationToken)
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
            if (deliveryKey is not null && await db.WebhookEvents.AnyAsync(e => e.DeliveryKey == deliveryKey, cancellationToken))
            {
                return PaymentRecoveryResult.Observed;
            }

            var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
            var order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            var now = clock.UtcNow;
            PaymentRecoveryExecution? execution = null;
            if (recovery is not null)
            {
                execution = await db.PaymentRecoveryExecutions.SingleOrDefaultAsync(e => e.Id == recovery.RequestId && e.PaymentId == paymentId, cancellationToken);
                if (execution is null || execution.State != PaymentRecoveryExecutionState.Running
                    || execution.Source != (recovery.Operation is null ? PaymentRecoverySource.Automatic : PaymentRecoverySource.Manual))
                {
                    // A superseded owner may not apply its delayed response or finish the replacement execution.
                    return PaymentRecoveryResult.Interrupted;
                }

                if (now >= execution.ExpiresAtUtc)
                {
                    execution.Interrupt(now);
                    AddRecoveryAudit(paymentId, recovery, PaymentRecoveryResult.Interrupted, now);
                    await db.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return PaymentRecoveryResult.Interrupted;
                }
            }

            if (recovery?.Operation is { } operation && !await PaymentRecoveryService.IsAdminAsync(db, operation.ActorUserId, cancellationToken))
            {
                execution!.Complete(now, PaymentRecoveryOutcome.Forbidden);
                AddRecoveryAudit(paymentId, recovery, PaymentRecoveryResult.Forbidden, now);
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return PaymentRecoveryResult.Forbidden;
            }

            if (recovery is not null && (payment.ConcurrencyVersion != recovery.PaymentVersion
                || order.ConcurrencyVersion != recovery.OrderVersion
                || !PaymentRecoveryService.IsEligible(payment)))
            {
                execution!.Complete(now, PaymentRecoveryOutcome.Conflict);
                AddRecoveryAudit(paymentId, recovery, PaymentRecoveryResult.Conflict, now);
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return PaymentRecoveryResult.Conflict;
            }

            if (recovery is not null && response.Status is not (PaymentOrderQueryStatus.Found or PaymentOrderQueryStatus.InvalidResponse))
            {
                execution!.Complete(now, PaymentRecoveryOutcome.Unavailable);
                AddRecoveryAudit(paymentId, recovery, PaymentRecoveryResult.Unavailable, now);
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return PaymentRecoveryResult.Unavailable;
            }

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

            var result = outcome switch
            {
                WebhookOutcome.Confirmed => PaymentRecoveryResult.Confirmed,
                WebhookOutcome.RequiresAttention => PaymentRecoveryResult.RequiresAttention,
                _ => PaymentRecoveryResult.Observed,
            };
            if (deliveryKey is not null)
            {
                db.WebhookEvents.Add(new WebhookEvent(deliveryKey, payment.Id, externalId, outcome, now));
            }

            if (recovery is not null)
            {
                execution!.Complete(now, result switch
                {
                    PaymentRecoveryResult.Confirmed => PaymentRecoveryOutcome.Confirmed,
                    PaymentRecoveryResult.RequiresAttention => PaymentRecoveryOutcome.RequiresAttention,
                    _ => PaymentRecoveryOutcome.Observed,
                });
                AddRecoveryAudit(payment.Id, recovery, result, now);
            }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
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
    }

    private void AddRecoveryAudit(long paymentId, RecoveryEvidence evidence, PaymentRecoveryResult result, DateTimeOffset now)
    {
        if (evidence.Operation is not { } operation) { return; }
        db.AuditLogs.Add(new AuditLog(operation.ActorUserId, "payment.recovery.completed", nameof(Payment),
            paymentId.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(new
            {
                evidence.RequestId,
                Reason = operation.Reason.ToString(),
                Result = result.ToString(),
            }), operation.CorrelationId, now));
    }
}

internal sealed record RecoveryEvidence(Guid RequestId, Guid PaymentVersion, Guid OrderVersion, PaymentRecoveryOperation? Operation);
