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

internal sealed class PaymentRefundService(SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> options, IClock clock) : IPaymentRefundService
{
    public async Task<PaymentRefundResult> SendAsync(long paymentId, Guid expectedRefundVersion, PaymentRefundActor actor, CancellationToken cancellationToken = default)
    {
        if (Validate(paymentId, expectedRefundVersion, actor) is { } invalid) { return invalid; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await PaymentRecoveryService.IsAdminAsync(db, actor.UserId, cancellationToken)) { return PaymentRefundResult.Forbidden; }
            var request = await db.PaymentRefundRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PaymentId == paymentId, cancellationToken);
            if (request is null) { return PaymentRefundResult.NotFound; }
            if (request.State != PaymentRefundRequestState.Prepared) { return Existing(request.State); }
            if (request.ConcurrencyVersion != expectedRefundVersion) { return PaymentRefundResult.Conflict; }
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, cancellationToken);
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            if (!Matches(request, payment, order)) { return PaymentRefundResult.Conflict; }
            var canonical = await gateway.GetOrderAsync(new(payment.ExternalOrderId!, payment.Environment, payment.ExternalReference, payment.Amount, payment.Currency), cancellationToken);
            if (canonical.Status != PaymentOrderQueryStatus.Found || canonical.Observation is not { } observation)
            { return PaymentRefundResult.Unavailable; }
            if (observation.Refund is not null || observation.State != ObservedOrderState.Processed || observation.ExternalOrderId != request.ExternalOrderId
                || observation.SettledPaymentId != request.ExternalPaymentId || observation.PaidAmount != request.Amount
                || observation.UpdatedAtUtc < payment.ProviderUpdatedAtUtc || observation.UpdatedAtUtc > clock.UtcNow.AddMinutes(5)
                || payment.DispatchStartedAtUtc is not { } dispatched || observation.CreatedAtUtc < dispatched.AddMinutes(-5))
            { return PaymentRefundResult.RequiresAttention; }
            // Persist irreversible local ownership after re-reading versions and Admin membership; no HTTP in transaction.
            var claim = await ClaimAsync(paymentId, expectedRefundVersion, actor, cancellationToken);
            if (claim is not null) { return claim.Value; }
            // Disconnect/cancel after the durable claim cannot authorize another POST. Bounded independent token.
            using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.TimeoutSeconds + 15));
            PaymentRefundSubmitResult response;
            try { response = await gateway.RefundOrderAsync(new(request.Id, request.ExternalOrderId, request.Environment), operation.Token); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { response = PaymentRefundSubmitResult.Unknown; }
            return await RecordSubmissionAsync(paymentId, actor, response);
        }
        catch (OperationCanceledException) { db.ChangeTracker.Clear(); throw; }
        catch (Exception ex) when (ex is DbUpdateException or NpgsqlException or HttpRequestException || PaymentValidation.IsConflict(ex))
        { db.ChangeTracker.Clear(); return PaymentRefundResult.Unavailable; }
    }

    public async Task<PaymentRefundResult> CheckAsync(long paymentId, Guid expectedRefundVersion, PaymentRefundActor actor, CancellationToken cancellationToken = default)
    {
        if (Validate(paymentId, expectedRefundVersion, actor) is { } invalid) { return invalid; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await PaymentRecoveryService.IsAdminAsync(db, actor.UserId, cancellationToken)) { return PaymentRefundResult.Forbidden; }
            var request = await db.PaymentRefundRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PaymentId == paymentId, cancellationToken);
            if (request is null) { return PaymentRefundResult.NotFound; }
            if (request.State == PaymentRefundRequestState.Confirmed) { return PaymentRefundResult.Confirmed; }
            if (request.State is not (PaymentRefundRequestState.Sending or PaymentRefundRequestState.AwaitingConfirmation)) { return PaymentRefundResult.NotEligible; }
            if (request.ConcurrencyVersion != expectedRefundVersion) { return PaymentRefundResult.Conflict; }
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, cancellationToken);
            var orderVersion = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.ConcurrencyVersion).SingleAsync(cancellationToken);
            var response = await gateway.GetOrderAsync(new(request.ExternalOrderId, request.Environment, payment.ExternalReference, request.Amount, request.Currency), cancellationToken);
            var result = await new PaymentObservationProcessor(db, clock).ApplyAsync(paymentId, request.ExternalOrderId, null, response, null, cancellationToken,
                new(request.Id, expectedRefundVersion, payment.ConcurrencyVersion, orderVersion, actor));
            return result switch
            {
                PaymentRecoveryResult.Refunded => PaymentRefundResult.Confirmed,
                PaymentRecoveryResult.Forbidden => PaymentRefundResult.Forbidden,
                PaymentRecoveryResult.Conflict => PaymentRefundResult.Conflict,
                PaymentRecoveryResult.RequiresAttention => PaymentRefundResult.RequiresAttention,
                PaymentRecoveryResult.Unavailable => PaymentRefundResult.Unavailable,
                _ => PaymentRefundResult.AwaitingConfirmation,
            };
        }
        catch (OperationCanceledException) { db.ChangeTracker.Clear(); throw; }
        catch (Exception ex) when (ex is DbUpdateException or NpgsqlException or HttpRequestException || PaymentValidation.IsConflict(ex))
        { db.ChangeTracker.Clear(); return PaymentRefundResult.Unavailable; }
    }

    private async Task<PaymentRefundResult?> ClaimAsync(long paymentId, Guid version, PaymentRefundActor actor, CancellationToken cancellationToken)
    {
        var relational = db.Database.IsRelational();
        if (!relational) { await PaymentObservationProcessor.InMemoryLock.WaitAsync(cancellationToken); }
        try
        {
            await using var tx = relational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            db.ChangeTracker.Clear();
            if (!await PaymentRecoveryService.IsAdminAsync(db, actor.UserId, cancellationToken)) { return PaymentRefundResult.Forbidden; }
            var request = await db.PaymentRefundRequests.SingleAsync(r => r.PaymentId == paymentId, cancellationToken);
            if (request.State != PaymentRefundRequestState.Prepared) { return Existing(request.State); }
            if (request.ConcurrencyVersion != version) { return PaymentRefundResult.Conflict; }
            var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
            var order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            if (!Matches(request, payment, order) || clock.UtcNow < payment.UpdatedAtUtc || clock.UtcNow < order.UpdatedAtUtc || clock.UtcNow < request.CreatedAtUtc)
            { return PaymentRefundResult.Conflict; }
            request.Begin(clock.UtcNow);
            Audit(paymentId, request.Id, actor, "payment.refund.dispatch.started", "Sending");
            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) { await tx.CommitAsync(cancellationToken); }
            return null;
        }
        finally { if (!relational) { PaymentObservationProcessor.InMemoryLock.Release(); } }
    }

    private async Task<PaymentRefundResult> RecordSubmissionAsync(long paymentId, PaymentRefundActor actor, PaymentRefundSubmitResult response)
    {
        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = persistence.Token;
        var relational = db.Database.IsRelational();
        if (!relational) { await PaymentObservationProcessor.InMemoryLock.WaitAsync(token); }
        try
        {
            await using var tx = relational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            db.ChangeTracker.Clear();
            var request = await db.PaymentRefundRequests.SingleAsync(r => r.PaymentId == paymentId, token);
            if (request.State == PaymentRefundRequestState.Sending)
            {
                request.AwaitConfirmation();
                Audit(paymentId, request.Id, actor, "payment.refund.dispatch.completed", response.ToString());
                await db.SaveChangesAsync(token);
                if (tx is not null) { await tx.CommitAsync(token); }
            }
            return Existing(request.State);
        }
        finally { if (!relational) { PaymentObservationProcessor.InMemoryLock.Release(); } }
    }

    private PaymentRefundResult? Validate(long paymentId, Guid version, PaymentRefundActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!options.Value.RefundEnabled || !MercadoPagoOptions.IsValid(options.Value)) { return PaymentRefundResult.Disabled; }
        if (paymentId <= 0 || version == Guid.Empty || actor.UserId == Guid.Empty || string.IsNullOrWhiteSpace(actor.CorrelationId)
            || actor.CorrelationId.Length > AuditLog.CorrelationIdMaxLength || !actor.CorrelationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        { return PaymentRefundResult.Invalid; }
        return db.ChangeTracker.HasChanges() ? PaymentRefundResult.Conflict : null;
    }

    private static bool Matches(PaymentRefundRequest r, Payment p, Sallvat.Domain.Orders.Order o) => PaymentRefundRequest.CanPrepare(p, o)
        && r.PaymentVersion == p.ConcurrencyVersion && r.OrderVersion == o.ConcurrencyVersion && r.ExternalOrderId == p.ExternalOrderId
        && r.ExternalPaymentId == p.ExternalPaymentId && r.Amount == p.Amount && r.Currency == p.Currency && r.Environment == p.Environment;

    private static PaymentRefundResult Existing(PaymentRefundRequestState state) => state switch
    {
        PaymentRefundRequestState.Confirmed => PaymentRefundResult.Confirmed,
        PaymentRefundRequestState.Sending or PaymentRefundRequestState.AwaitingConfirmation => PaymentRefundResult.AwaitingConfirmation,
        _ => PaymentRefundResult.RequiresAttention,
    };

    private void Audit(long paymentId, Guid id, PaymentRefundActor actor, string action, string outcome) =>
        db.AuditLogs.Add(new(actor.UserId, action, nameof(Payment), paymentId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { RequestId = id, Outcome = outcome }), actor.CorrelationId, clock.UtcNow));
}
