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

internal sealed class PaymentRecoveryService(
    SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> configuredOptions, IClock clock) : IPaymentRecoveryService
{
    public Task<PaymentRecoveryResult> RecoverAsync(long paymentId, Guid expectedVersion,
        PaymentRecoveryOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RecoverCoreAsync(paymentId, expectedVersion, operation, cancellationToken);
    }

    internal Task<PaymentRecoveryResult> RecoverAutomaticallyAsync(long paymentId, Guid expectedVersion, CancellationToken cancellationToken) =>
        RecoverCoreAsync(paymentId, expectedVersion, null, cancellationToken);

    private async Task<PaymentRecoveryResult> RecoverCoreAsync(long paymentId, Guid expectedVersion,
        PaymentRecoveryOperation? operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.RecoveryEnabled || (operation is null && !options.AutomaticRecoveryEnabled) || !MercadoPagoOptions.IsValid(options))
        {
            return PaymentRecoveryResult.Disabled;
        }

        if (paymentId <= 0 || expectedVersion == Guid.Empty || (operation is not null && (operation.ActorUserId == Guid.Empty || !Enum.IsDefined(operation.Reason)
            || string.IsNullOrWhiteSpace(operation.CorrelationId) || operation.CorrelationId.Length > AuditLog.CorrelationIdMaxLength
            || !operation.CorrelationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
        {
            return PaymentRecoveryResult.Invalid;
        }

        if (db.ChangeTracker.HasChanges())
        {
            return PaymentRecoveryResult.Conflict;
        }

        try
        {
            if (operation is not null && !await IsAdminAsync(db, operation.ActorUserId, cancellationToken))
            {
                return PaymentRecoveryResult.Forbidden;
            }

            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
            if (payment is null)
            {
                return PaymentRecoveryResult.NotFound;
            }

            if (payment.ConcurrencyVersion != expectedVersion)
            {
                return PaymentRecoveryResult.Conflict;
            }

            if (!IsEligible(payment))
            {
                // A missing ID or review state never authorizes search-and-bind, a new POST, or manual approval.
                return PaymentRecoveryResult.NotEligible;
            }

            var orderVersion = await db.Orders.AsNoTracking().Where(o => o.Id == payment.OrderId)
                .Select(o => o.ConcurrencyVersion).SingleAsync(cancellationToken);
            var evidence = new RecoveryEvidence(Guid.NewGuid(), payment.ConcurrencyVersion, orderVersion, operation);
            var claimed = await ClaimAsync(paymentId, evidence, cancellationToken);
            if (claimed is not null)
            {
                return claimed.Value;
            }

            // Persist execution (and human audit when applicable) before GET; never issue a financial POST.
            var response = await gateway.GetOrderAsync(new(payment.ExternalOrderId!, payment.Environment,
                payment.ExternalReference, payment.Amount, payment.Currency), cancellationToken);
            return await new PaymentObservationProcessor(db, clock).ApplyAsync(payment.Id, payment.ExternalOrderId!,
                null, response, evidence, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or HttpRequestException || PaymentValidation.IsConflict(exception))
        {
            db.ChangeTracker.Clear();
            return PaymentRecoveryResult.Unavailable;
        }
    }

    private async Task<PaymentRecoveryResult?> ClaimAsync(long paymentId, RecoveryEvidence evidence, CancellationToken cancellationToken)
    {
        var relational = db.Database.IsRelational();
        if (!relational)
        {
            await PaymentObservationProcessor.InMemoryLock.WaitAsync(cancellationToken);
        }

        try
        {
            await using var transaction = relational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            db.ChangeTracker.Clear();
            if (evidence.Operation is { } operation && !await IsAdminAsync(db, operation.ActorUserId, cancellationToken))
            {
                return PaymentRecoveryResult.Forbidden;
            }

            var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
            var orderVersion = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.ConcurrencyVersion).SingleAsync(cancellationToken);
            if (!IsEligible(payment) || payment.ConcurrencyVersion != evidence.PaymentVersion || orderVersion != evidence.OrderVersion)
            {
                return PaymentRecoveryResult.Conflict;
            }

            var now = clock.UtcNow;
            if (evidence.Operation is null && !await PaymentRecoverySchedule.Due(db, now).AnyAsync(p => p.Id == paymentId, cancellationToken))
            {
                return PaymentRecoveryResult.NotEligible;
            }
            var running = await db.PaymentRecoveryExecutions.SingleOrDefaultAsync(e => e.PaymentId == paymentId
                && e.State == PaymentRecoveryExecutionState.Running, cancellationToken);
            if (running is not null)
            {
                if (now < running.ExpiresAtUtc)
                {
                    return PaymentRecoveryResult.Busy;
                }

                running.Interrupt(now);
                AddAudit(paymentId, evidence with { RequestId = running.Id }, "payment.recovery.interrupted", PaymentRecoveryResult.Interrupted);
                // Release the unique running slot before INSERT; both writes remain in this transaction.
                await db.SaveChangesAsync(cancellationToken);
            }

            db.PaymentRecoveryExecutions.Add(new(evidence.RequestId, paymentId, now,
                evidence.Operation is null ? PaymentRecoverySource.Automatic : PaymentRecoverySource.Manual));
            AddAudit(paymentId, evidence, "payment.recovery.requested", null);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }
        finally
        {
            if (!relational)
            {
                PaymentObservationProcessor.InMemoryLock.Release();
            }
        }
    }

    internal static bool IsEligible(Payment payment) => payment.Provider == "MercadoPago"
        && payment.Environment == PaymentEnvironment.Sandbox && payment.Status == PaymentStatus.Pending
        && payment.DispatchState == PaymentDispatchState.Completed && payment.ExternalOrderId is not null
        && payment.PreferenceId is null && payment.ExternalPaymentId is null;

    internal static Task<bool> IsAdminAsync(SallvatDbContext db, Guid actorId, CancellationToken cancellationToken) =>
        (from userRole in db.UserRoles
         join role in db.Roles on userRole.RoleId equals role.Id
         join user in db.Users on userRole.UserId equals user.Id
         where user.Id == actorId && role.NormalizedName == "ADMIN"
         select user.Id).AnyAsync(cancellationToken);

    private void AddAudit(long paymentId, RecoveryEvidence evidence, string action, PaymentRecoveryResult? result)
    {
        if (evidence.Operation is not { } operation) { return; }
        db.AuditLogs.Add(new AuditLog(operation.ActorUserId, action, nameof(Payment),
            paymentId.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(new
            {
                evidence.RequestId,
                Reason = operation.Reason.ToString(),
                Result = result?.ToString(),
            }), operation.CorrelationId, clock.UtcNow));
    }
}
