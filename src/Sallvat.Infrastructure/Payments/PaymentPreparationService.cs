using System.Data;
using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentPreparationService(SallvatDbContext dbContext, IClock clock) : IPaymentPreparationService
{
    // InMemory tests do not implement transactions or unique indexes. Production relies on PostgreSQL.
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);

    public async Task<PaymentPreparationResult> PrepareAsync(
        long orderId,
        CartOwner owner,
        PaymentEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (orderId <= 0 || environment != PaymentEnvironment.Sandbox)
        {
            return new(PaymentPreparationStatus.Invalid);
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            return new(PaymentPreparationStatus.Conflict);
        }

        var relational = dbContext.Database.IsRelational();
        if (!relational)
        {
            await InMemoryLock.WaitAsync(cancellationToken);
        }

        try
        {
            return await PrepareCoreAsync(orderId, owner, environment, cancellationToken);
        }
        finally
        {
            if (!relational)
            {
                InMemoryLock.Release();
            }
        }
    }

    private async Task<PaymentPreparationResult> PrepareCoreAsync(
        long orderId,
        CartOwner owner,
        PaymentEnvironment environment,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var now = clock.UtcNow;
            var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
            if (order is null)
            {
                return new(PaymentPreparationStatus.NotFound);
            }

            await dbContext.Entry(order).ReloadAsync(cancellationToken);
            if (dbContext.Entry(order).State == EntityState.Detached
                || !await PaymentValidation.OwnsOrderAsync(dbContext, order, owner, now, cancellationToken))
            {
                return new(PaymentPreparationStatus.NotFound);
            }

            if (order.Status != OrderStatus.PendingPayment || order.ExpiresAtUtc <= now || order.UpdatedAtUtc > now)
            {
                return new(PaymentPreparationStatus.Invalid);
            }

            var items = await dbContext.OrderItems.AsNoTracking().Where(item => item.OrderId == orderId).ToListAsync(cancellationToken);
            var reservations = await dbContext.StockReservations.AsNoTracking()
                .Where(reservation => reservation.OrderId == orderId).ToListAsync(cancellationToken);
            if (!PaymentValidation.HasValidSnapshots(order, items, reservations, now))
            {
                return new(PaymentPreparationStatus.Invalid);
            }

            var existing = await dbContext.Payments.AsNoTracking()
                .Where(payment => payment.OrderId == orderId
                    && (payment.Status == PaymentStatus.Created || payment.Status == PaymentStatus.Pending
                        || payment.Status == PaymentStatus.Approved || payment.Status == PaymentStatus.RequiresAttention))
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                if (existing.Environment != environment || existing.Status is PaymentStatus.RequiresAttention or PaymentStatus.Approved
                    || existing.Amount != order.GrandTotal || existing.Currency != order.Currency
                    || existing.ExternalReference != order.OrderNumber || existing.ExpiresAtUtc != order.ExpiresAtUtc)
                {
                    return new(PaymentPreparationStatus.RequiresAttention);
                }

                return new(PaymentPreparationStatus.AlreadyPrepared, existing.Id);
            }

            now = clock.UtcNow;
            if (now >= order.ExpiresAtUtc || now < order.UpdatedAtUtc)
            {
                return new(PaymentPreparationStatus.Invalid);
            }

            var payment = new Payment(order, environment, Guid.NewGuid(), now);
            order.RegisterPaymentAttempt(now);
            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new(PaymentPreparationStatus.Prepared, payment.Id);
        }
        catch (Exception exception) when (PaymentValidation.IsConflict(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            dbContext.ChangeTracker.Clear();
            return new(PaymentPreparationStatus.Conflict);
        }
    }
}
