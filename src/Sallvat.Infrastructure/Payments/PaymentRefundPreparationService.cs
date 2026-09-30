using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentRefundPreparationService(SallvatDbContext db, IOptions<MercadoPagoOptions> options, IClock clock)
    : IPaymentRefundPreparationService
{
    public async Task<PaymentRefundPreparationResult> PrepareAsync(long paymentId, Guid expectedPaymentVersion, Guid expectedOrderVersion,
        PaymentRefundOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.RefundPreparationEnabled || !MercadoPagoOptions.IsValid(options.Value)) { return new(PaymentRefundPreparationStatus.Disabled); }
        if (paymentId <= 0 || expectedPaymentVersion == Guid.Empty || expectedOrderVersion == Guid.Empty
            || operation.ActorUserId == Guid.Empty || !Enum.IsDefined(operation.Reason)
            || string.IsNullOrWhiteSpace(operation.CorrelationId) || operation.CorrelationId.Length > AuditLog.CorrelationIdMaxLength
            || !operation.CorrelationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            return new(PaymentRefundPreparationStatus.Invalid);
        }
        if (db.ChangeTracker.HasChanges()) { return new(PaymentRefundPreparationStatus.Conflict); }
        var relational = db.Database.IsRelational();
        if (!relational) { await PaymentObservationProcessor.InMemoryLock.WaitAsync(cancellationToken); }
        try
        {
            await using var transaction = relational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            db.ChangeTracker.Clear();
            if (!await PaymentRecoveryService.IsAdminAsync(db, operation.ActorUserId, cancellationToken)) { return new(PaymentRefundPreparationStatus.Forbidden); }
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
            if (payment is null) { return new(PaymentRefundPreparationStatus.NotFound); }
            var existing = await db.PaymentRefundRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PaymentId == paymentId, cancellationToken);
            // Replay returns the immutable original intention, never replaces actor/reason/snapshots or issues HTTP.
            if (existing is not null) { return new(PaymentRefundPreparationStatus.AlreadyPrepared, existing.Id); }
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            if (payment.ConcurrencyVersion != expectedPaymentVersion || order.ConcurrencyVersion != expectedOrderVersion)
            {
                return new(PaymentRefundPreparationStatus.Conflict);
            }
            if (!PaymentRefundRequest.CanPrepare(payment, order) || clock.UtcNow < payment.UpdatedAtUtc || clock.UtcNow < order.UpdatedAtUtc)
            {
                return new(PaymentRefundPreparationStatus.NotEligible);
            }
            var request = new PaymentRefundRequest(Guid.NewGuid(), payment, order, operation.ActorUserId, operation.Reason, clock.UtcNow);
            db.PaymentRefundRequests.Add(request);
            db.AuditLogs.Add(new(operation.ActorUserId, "payment.refund.prepared", nameof(Payment),
                paymentId.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(new
                {
                    RequestId = request.Id,
                    Reason = request.Reason.ToString(),
                    request.Amount,
                    request.Currency,
                    request.PaymentVersion,
                    request.OrderVersion,
                }), operation.CorrelationId, request.CreatedAtUtc));
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) { await transaction.CommitAsync(cancellationToken); }
            return new(PaymentRefundPreparationStatus.Prepared, request.Id);
        }
        catch (Exception exception) when (PaymentValidation.IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return new(PaymentRefundPreparationStatus.Conflict);
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException)
        {
            db.ChangeTracker.Clear();
            return new(PaymentRefundPreparationStatus.Unavailable);
        }
        finally { if (!relational) { PaymentObservationProcessor.InMemoryLock.Release(); } }
    }
}
