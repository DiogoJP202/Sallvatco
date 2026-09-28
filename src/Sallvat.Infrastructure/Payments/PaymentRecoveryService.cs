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
    public async Task<PaymentRecoveryResult> RecoverAsync(long paymentId, Guid expectedVersion,
        PaymentRecoveryOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.RecoveryEnabled || !MercadoPagoOptions.IsValid(options))
        {
            return PaymentRecoveryResult.Disabled;
        }

        if (paymentId <= 0 || expectedVersion == Guid.Empty || operation.ActorUserId == Guid.Empty || !Enum.IsDefined(operation.Reason)
            || string.IsNullOrWhiteSpace(operation.CorrelationId) || operation.CorrelationId.Length > AuditLog.CorrelationIdMaxLength
            || !operation.CorrelationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            return PaymentRecoveryResult.Invalid;
        }

        if (db.ChangeTracker.HasChanges())
        {
            return PaymentRecoveryResult.Conflict;
        }

        try
        {
            if (!await IsAdminAsync(db, operation.ActorUserId, cancellationToken))
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
            AddAudit(paymentId, evidence, "payment.recovery.requested", null);
            await db.SaveChangesAsync(cancellationToken);

            // Persist intent before GET. A crash here leaves an open audit, never an untracked financial POST.
            var response = await gateway.GetOrderAsync(new(payment.ExternalOrderId!, payment.Environment,
                payment.ExternalReference, payment.Amount, payment.Currency), cancellationToken);
            if (response.Status is not (PaymentOrderQueryStatus.Found or PaymentOrderQueryStatus.InvalidResponse))
            {
                AddAudit(paymentId, evidence, "payment.recovery.completed", PaymentRecoveryResult.Unavailable);
                await db.SaveChangesAsync(cancellationToken);
                return PaymentRecoveryResult.Unavailable;
            }

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

    private void AddAudit(long paymentId, RecoveryEvidence evidence, string action, PaymentRecoveryResult? result) =>
        db.AuditLogs.Add(new AuditLog(evidence.Operation.ActorUserId, action, nameof(Payment),
            paymentId.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(new
            {
                evidence.RequestId,
                Reason = evidence.Operation.Reason.ToString(),
                Result = result?.ToString(),
            }), evidence.Operation.CorrelationId, clock.UtcNow));
}
