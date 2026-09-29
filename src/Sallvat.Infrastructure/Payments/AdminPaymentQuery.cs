using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class AdminPaymentQuery(SallvatDbContext db, IOptions<MercadoPagoOptions> options, IClock clock) : IAdminPaymentQuery
{
    public async Task<AdminPaymentPage> ListAsync(AdminPaymentFilter filter, long? beforeId = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(filter) || beforeId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }

        var payments = db.Payments.AsNoTracking();
        payments = filter switch
        {
            AdminPaymentFilter.Attention => payments.Where(p => p.Status == PaymentStatus.RequiresAttention
                || p.DispatchState == PaymentDispatchState.RequiresAttention || p.DispatchState == PaymentDispatchState.Sending),
            AdminPaymentFilter.Pending => payments.Where(p => (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.Pending)
                && p.DispatchState != PaymentDispatchState.Sending && p.DispatchState != PaymentDispatchState.RequiresAttention),
            AdminPaymentFilter.Approved => payments.Where(p => p.Status == PaymentStatus.Approved),
            _ => payments,
        };
        if (beforeId is { } cursor)
        {
            payments = payments.Where(p => p.Id < cursor);
        }

        // Keyset pagination: bounded work and stable ordering even with equal timestamps.
        var items = await (from p in payments
                           join o in db.Orders.AsNoTracking() on p.OrderId equals o.Id
                           orderby p.Id descending
                           select new AdminPaymentSummary(p.Id, o.OrderNumber, o.Status, p.Environment,
                               p.Status, p.DispatchState, p.AttentionReason, p.Amount, p.Currency, p.CreatedAtUtc, p.UpdatedAtUtc))
            .Take(26).ToListAsync(cancellationToken);
        return new(filter, items.Take(25).ToArray(), items.Count > 25 ? items[24].Id : null);
    }

    public async Task<AdminPaymentDetails?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        var detail = await (from p in db.Payments.AsNoTracking()
                            join o in db.Orders.AsNoTracking() on p.OrderId equals o.Id
                            where p.Id == id
                            select new
                            {
                                Summary = new AdminPaymentSummary(p.Id, o.OrderNumber, o.Status, p.Environment,
                                    p.Status, p.DispatchState, p.AttentionReason, p.Amount, p.Currency, p.CreatedAtUtc, p.UpdatedAtUtc),
                                p.ExternalOrderId,
                                p.ExternalPaymentId,
                                p.DispatchStartedAtUtc,
                                p.ExpiresAtUtc,
                                p.ConfirmedAtUtc,
                                p.ProviderUpdatedAtUtc,
                                p.ConcurrencyVersion,
                                p.Provider,
                                p.PreferenceId,
                            }).SingleOrDefaultAsync(cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var receipts = await db.WebhookEvents.AsNoTracking().Where(e => e.PaymentId == id)
            .OrderByDescending(e => e.ReceivedAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new AdminPaymentReceipt(e.ReceivedAtUtc, e.Outcome)).Take(51).ToListAsync(cancellationToken);
        var entityId = id.ToString(CultureInfo.InvariantCulture);
        var history = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(Payment) && a.EntityId == entityId
                && (a.Action == "payment.recovery.requested" || a.Action == "payment.recovery.completed" || a.Action == "payment.recovery.interrupted"))
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new { a.CreatedAtUtc, a.Action, a.ChangesJson }).Take(51).ToListAsync(cancellationToken);
        var enabled = options.Value.RecoveryEnabled && MercadoPagoOptions.IsValid(options.Value);
        var executions = await db.PaymentRecoveryExecutions.AsNoTracking().Where(e => e.PaymentId == id)
            .OrderByDescending(e => e.StartedAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new AdminRecoveryExecution(e.Id, e.State, e.StartedAtUtc, e.ExpiresAtUtc, e.FinishedAtUtc, e.Source, e.Outcome))
            .Take(21).ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var blockedUntil = await db.PaymentRecoveryExecutions.AsNoTracking().Where(e => e.PaymentId == id
            && e.State == PaymentRecoveryExecutionState.Running && e.ExpiresAtUtc > now)
            .Select(e => (DateTimeOffset?)e.ExpiresAtUtc).SingleOrDefaultAsync(cancellationToken);
        var eligible = detail.Provider == "MercadoPago" && detail.Summary.Environment == PaymentEnvironment.Sandbox
            && detail.Summary.Status == PaymentStatus.Pending && detail.Summary.DispatchState == PaymentDispatchState.Completed
            && detail.ExternalOrderId is not null && detail.PreferenceId is null && detail.ExternalPaymentId is null;
        return new(detail.Summary, detail.ExternalOrderId, detail.ExternalPaymentId, detail.DispatchStartedAtUtc,
            detail.ExpiresAtUtc, detail.ConfirmedAtUtc, detail.ProviderUpdatedAtUtc, receipts.Take(50).ToArray(), receipts.Count > 50,
            detail.ConcurrencyVersion, enabled, enabled && eligible && blockedUntil is null,
            history.Take(50).Select(a => ReadEntry(a.CreatedAtUtc, a.Action, a.ChangesJson)).ToArray(), history.Count > 50,
            executions.Take(20).ToArray(), executions.Count > 20, blockedUntil,
            enabled && options.Value.AutomaticRecoveryEnabled,
            await db.PaymentRecoveryExecutions.CountAsync(e => e.PaymentId == id && e.Source == PaymentRecoverySource.Automatic, cancellationToken));
    }

    private static AdminRecoveryEntry ReadEntry(DateTimeOffset timestamp, string action, string json)
    {
        var completion = action != "payment.recovery.requested";
        if (json.Length <= 4096)
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("RequestId", out var id)
                    && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var requestId) && requestId != Guid.Empty)
                {
                    return new(timestamp, requestId, completion, ReadEnum<PaymentRecoveryReason>(root, "Reason"),
                        completion ? ReadEnum<PaymentRecoveryResult>(root, "Result") : null);
                }
            }
            catch (JsonException) { }
        }

        // Never return arbitrary audit JSON or unknown strings to the page.
        return new(timestamp, null, completion, null, null);
    }

    private static T? ReadEnum<T>(JsonElement root, string property) where T : struct, Enum =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && Enum.TryParse<T>(value.GetString(), out var parsed) && Enum.GetName(parsed) == value.GetString() ? parsed : null;
}
