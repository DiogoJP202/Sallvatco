using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentReconciliationService(
    SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> configuredOptions, IClock clock) : IPaymentReconciliationService
{
    public async Task<PaymentReconciliationResult> InspectAsync(long paymentId, CartOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        if (!configuredOptions.Value.OrdersEnabled || !MercadoPagoOptions.IsValid(configuredOptions.Value))
        {
            return new(PaymentReconciliationStatus.Disabled);
        }

        if (db.ChangeTracker.HasChanges())
        {
            return new(PaymentReconciliationStatus.Conflict);
        }

        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        var order = payment is null ? null : await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);
        if (payment is null || order is null || !await PaymentValidation.OwnsOrderAsync(db, order, owner, clock.UtcNow, cancellationToken))
        {
            return new(PaymentReconciliationStatus.NotFound);
        }

        if (payment.Environment != PaymentEnvironment.Sandbox || payment.Provider != "MercadoPago"
            || payment.PreferenceId is not null || payment.Amount != order.GrandTotal || payment.Currency != order.Currency
            || payment.ExternalReference != order.OrderNumber || payment.ExpiresAtUtc != order.ExpiresAtUtc)
        {
            return Review(PaymentReconciliationReason.LocalMismatch);
        }

        if (payment.ExternalOrderId is null)
        {
            // Reference alone cannot prove which attempt created a provider order. Never search-and-bind or POST here.
            return Review(PaymentReconciliationReason.MissingExternalOrderId);
        }

        var response = await gateway.GetOrderAsync(new(payment.ExternalOrderId, payment.Environment,
            payment.ExternalReference, payment.Amount, payment.Currency), cancellationToken);

        // Query is outside any DB transaction. Re-read to detect cancellation/expiry/linking during HTTP.
        var latestPayment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        var latestOrder = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == order.Id, cancellationToken);
        var now = clock.UtcNow;
        if (latestOrder is null || latestPayment is null || !await PaymentValidation.OwnsOrderAsync(db, latestOrder, owner, now, cancellationToken))
        {
            return new(PaymentReconciliationStatus.NotFound);
        }

        if (latestPayment.ConcurrencyVersion != payment.ConcurrencyVersion || latestOrder.ConcurrencyVersion != order.ConcurrencyVersion)
        {
            return new(PaymentReconciliationStatus.Conflict);
        }

        if (response.Status == PaymentOrderQueryStatus.NotFound)
        {
            return Review(PaymentReconciliationReason.ProviderNotFound);
        }

        if (response.Status is PaymentOrderQueryStatus.InvalidRequest or PaymentOrderQueryStatus.InvalidResponse)
        {
            return Review(PaymentReconciliationReason.ProviderMismatch);
        }

        if (response.Status != PaymentOrderQueryStatus.Found || response.Observation is not { } observation)
        {
            return new(PaymentReconciliationStatus.Unavailable);
        }

        if (observation.ExternalOrderId != payment.ExternalOrderId || payment.DispatchStartedAtUtc is not { } started
            || observation.CreatedAtUtc < started.AddMinutes(-5))
        {
            return Review(PaymentReconciliationReason.ProviderMismatch);
        }

        if (observation.State != ObservedOrderState.Created || observation.PaidAmount != 0 || observation.HasTransactions)
        {
            // Even processed/accredited is only an observation until atomic financial reconciliation exists.
            return Review(PaymentReconciliationReason.FinancialActivityObserved);
        }

        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).ToListAsync(cancellationToken);
        var reservations = await db.StockReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(cancellationToken);
        return payment.Status == PaymentStatus.Pending && payment.DispatchState == PaymentDispatchState.Completed
            && order.Status == OrderStatus.PendingPayment && now < order.ExpiresAtUtc
            && PaymentValidation.HasValidSnapshots(order, items, reservations, now)
                ? new(PaymentReconciliationStatus.AwaitingPayment)
                : Review(PaymentReconciliationReason.LocalReviewRequired);
    }

    private static PaymentReconciliationResult Review(PaymentReconciliationReason reason) => new(PaymentReconciliationStatus.RequiresAttention, reason);
}
