using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Inventory;
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
                || !await OwnsOrderAsync(order, owner, now, cancellationToken))
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
            if (!HasValidSnapshots(order, items, reservations, now))
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
        catch (Exception exception) when (IsConflict(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            dbContext.ChangeTracker.Clear();
            return new(PaymentPreparationStatus.Conflict);
        }
    }

    private async Task<bool> OwnsOrderAsync(Order order, CartOwner owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (owner.ApplicationUserId is Guid userId)
        {
            return userId != Guid.Empty && string.IsNullOrEmpty(owner.GuestToken)
                && await dbContext.Customers.AnyAsync(customer => customer.Id == order.CustomerId
                    && customer.ApplicationUserId == userId, cancellationToken);
        }

        if (owner.GuestToken is not { Length: 43 } token
            || !token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        // This authorizes only the current guest checkout session, not historical lookup by email.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return await dbContext.Carts.AnyAsync(cart => cart.Id == order.SourceCartId
                && cart.GuestTokenHash == hash && cart.CustomerId == null && cart.ExpiresAtUtc > now, cancellationToken)
            && !await dbContext.Customers.AnyAsync(customer => customer.Id == order.CustomerId
                && customer.ApplicationUserId != null, cancellationToken);
    }

    private static bool HasValidSnapshots(Order order, List<OrderItem> items, List<StockReservation> reservations, DateTimeOffset now)
    {
        if (items.Count == 0 || items.Any(item => item.Currency != order.Currency)
            || items.Sum(item => item.UnitPrice * item.Quantity) != order.ItemsSubtotal
            || items.Sum(item => item.DiscountAmount) != order.DiscountTotal
            || items.Sum(item => item.Subtotal) + order.ShippingTotal != order.GrandTotal)
        {
            return false;
        }

        var quantities = items.GroupBy(item => item.ProductVariantId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Quantity));
        return reservations.Count == quantities.Count
            && reservations.Select(reservation => reservation.ProductVariantId).Distinct().Count() == reservations.Count
            && reservations.All(reservation => reservation.Status == StockReservationStatus.Reserved
                && reservation.ExpiresAtUtc > now && reservation.ExpiresAtUtc == order.ExpiresAtUtc
                && quantities.TryGetValue(reservation.ProductVariantId, out var quantity) && quantity == reservation.Quantity);
    }

    private static bool IsConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        // The provider may wrap serialization failures in its execution strategy.
        || exception.GetBaseException() is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
