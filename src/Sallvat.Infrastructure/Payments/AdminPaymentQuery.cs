using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Payments;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class AdminPaymentQuery(SallvatDbContext db) : IAdminPaymentQuery
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
                            }).SingleOrDefaultAsync(cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var receipts = await db.WebhookEvents.AsNoTracking().Where(e => e.PaymentId == id)
            .OrderByDescending(e => e.ReceivedAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new AdminPaymentReceipt(e.ReceivedAtUtc, e.Outcome)).Take(51).ToListAsync(cancellationToken);
        return new(detail.Summary, detail.ExternalOrderId, detail.ExternalPaymentId, detail.DispatchStartedAtUtc,
            detail.ExpiresAtUtc, detail.ConfirmedAtUtc, detail.ProviderUpdatedAtUtc, receipts.Take(50).ToArray(), receipts.Count > 50);
    }
}
