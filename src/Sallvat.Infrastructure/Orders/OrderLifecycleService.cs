using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sallvat.Application.Orders;
using Sallvat.Application.Time;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Orders;

internal sealed class OrderLifecycleService(
    SallvatDbContext dbContext,
    IClock clock) : IOrderLifecycleService
{
    private const int MaximumBatchSize = 500;
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);

    public async Task<IReadOnlyList<AdminOrderSummary>> ListOpenAdminAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == OrderStatus.PendingPayment
                || order.Status == OrderStatus.RequiresAttention)
            .OrderByDescending(order =>
                order.Status == OrderStatus.RequiresAttention)
            .ThenBy(order => order.ExpiresAtUtc)
            .Take(100)
            .Select(order => new AdminOrderSummary(
                order.Id,
                order.OrderNumber,
                order.Status,
                order.BuyerName,
                order.GrandTotal,
                order.Currency,
                order.CreatedAtUtc,
                order.ExpiresAtUtc,
                order.AttentionReason,
                order.AttentionSinceUtc,
                order.ConcurrencyVersion))
            .ToListAsync(cancellationToken);

    public async Task<OrderLifecycleMutationResult> TransitionAsync(
        long orderId,
        Guid concurrencyVersion,
        OrderStatus targetStatus,
        string reason,
        OrderAdminContext operation,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0
            || concurrencyVersion == Guid.Empty
            || operation.ActorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(operation.CorrelationId))
        {
            return Invalid("Os dados da operação administrativa são inválidos.");
        }

        if (targetStatus is not OrderStatus.RequiresAttention
            and not OrderStatus.Cancelled)
        {
            return Invalid(
                "Esta transição ainda não está disponível para operação manual.");
        }

        var normalizedReason = NormalizeReason(reason);
        if (normalizedReason is null)
        {
            return Invalid(
                $"Informe uma justificativa entre 5 e {Order.AttentionReasonMaxLength} caracteres.");
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            var order = await dbContext.Orders.SingleOrDefaultAsync(
                candidate => candidate.Id == orderId,
                cancellationToken);
            if (order is null)
            {
                return OrderLifecycleMutationResult.Failure(
                    OrderLifecycleMutationStatus.NotFound,
                    "O pedido não foi encontrado.");
            }

            if (order.Status == targetStatus)
            {
                await CommitAsync(transaction, cancellationToken);
                return OrderLifecycleMutationResult.Success(
                    wasAlreadyApplied: true);
            }

            if (order.ConcurrencyVersion != concurrencyVersion)
            {
                return OrderLifecycleMutationResult.Failure(
                    OrderLifecycleMutationStatus.ConcurrencyConflict,
                    "O pedido mudou. Recarregue a fila e tente novamente.");
            }

            if (!Order.CanTransition(order.Status, targetStatus))
            {
                return Invalid(
                    $"O pedido em {order.Status} não pode ir para {targetStatus}.");
            }

            var sourceStatus = order.Status;
            if (targetStatus == OrderStatus.Cancelled)
            {
                await ReleaseOrderHoldAsync(
                    order,
                    operation.ActorUserId,
                    normalizedReason,
                    cancellationToken);
            }

            order.TransitionTo(
                targetStatus,
                clock.UtcNow,
                targetStatus == OrderStatus.RequiresAttention
                    ? normalizedReason
                    : null);
            AddAudit(
                order,
                sourceStatus,
                targetStatus,
                normalizedReason,
                operation);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return OrderLifecycleMutationResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return OrderLifecycleMutationResult.Failure(
                OrderLifecycleMutationStatus.ConcurrencyConflict,
                "O pedido ou a reserva mudou. Recarregue e tente novamente.");
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidOperationException)
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Invalid(exception.Message);
        }
    }

    public async Task<int> ExpirePendingAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is <= 0 or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        if (!dbContext.Database.IsRelational())
        {
            await InMemoryLock.WaitAsync(cancellationToken);
        }

        try
        {
            var now = clock.UtcNow;
            var orderIds = await dbContext.Orders
                .AsNoTracking()
                .Where(order =>
                    order.Status == OrderStatus.PendingPayment
                    && order.ExpiresAtUtc <= now)
                .OrderBy(order => order.ExpiresAtUtc)
                .Select(order => order.Id)
                .Take(maximumItems)
                .ToListAsync(cancellationToken);
            var expired = 0;
            foreach (var orderId in orderIds)
            {
                if (await ExpireOneAsync(orderId, now, cancellationToken))
                {
                    expired++;
                }

                dbContext.ChangeTracker.Clear();
            }

            return expired;
        }
        finally
        {
            if (!dbContext.Database.IsRelational())
            {
                InMemoryLock.Release();
            }
        }
    }

    private async Task<bool> ExpireOneAsync(
        long orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            var order = await dbContext.Orders.SingleOrDefaultAsync(
                candidate => candidate.Id == orderId
                    && candidate.Status == OrderStatus.PendingPayment
                    && candidate.ExpiresAtUtc <= now,
                cancellationToken);
            if (order is null)
            {
                return false;
            }

            await ReleaseOrderHoldAsync(
                order,
                null,
                $"Reserva expirada do pedido {order.OrderNumber}",
                cancellationToken);
            order.TransitionTo(OrderStatus.Cancelled, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction, cancellationToken);
            return false;
        }
    }

    private async Task ReleaseOrderHoldAsync(
        Order order,
        Guid? actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var reservations = await dbContext.StockReservations
            .Where(reservation =>
                reservation.OrderId == order.Id
                && reservation.Status == StockReservationStatus.Reserved)
            .OrderBy(reservation => reservation.ProductVariantId)
            .ToListAsync(cancellationToken);
        var variantIds = reservations
            .Select(reservation => reservation.ProductVariantId)
            .ToArray();
        var variants = await dbContext.ProductVariants
            .Where(variant => variantIds.Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        foreach (var reservation in reservations)
        {
            if (!variants.TryGetValue(
                reservation.ProductVariantId,
                out var variant))
            {
                throw new InvalidOperationException(
                    "A variante reservada não foi encontrada.");
            }

            if (!reservation.Release(clock.UtcNow))
            {
                continue;
            }

            variant.ReleaseReservation(reservation.Quantity, clock.UtcNow);
            dbContext.InventoryMovements.Add(new InventoryMovement(
                variant.Id,
                InventoryMovementType.ReservationRelease,
                -reservation.Quantity,
                variant.OnHand,
                variant.Reserved,
                actorUserId,
                reason,
                clock.UtcNow));
        }

        var redemption = await dbContext.CouponRedemptions
            .SingleOrDefaultAsync(
                item => item.OrderId == order.Id,
                cancellationToken);
        if (redemption is null
            || !redemption.ReleaseForOrder(order.Id, clock.UtcNow))
        {
            return;
        }

        var coupon = await dbContext.Coupons.SingleAsync(
            item => item.Id == redemption.CouponId,
            cancellationToken);
        coupon.ReleaseUsage(clock.UtcNow);
    }

    private void AddAudit(
        Order order,
        OrderStatus sourceStatus,
        OrderStatus targetStatus,
        string reason,
        OrderAdminContext operation) =>
        dbContext.AuditLogs.Add(new AuditLog(
            operation.ActorUserId,
            "order.status.changed",
            nameof(Order),
            order.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new
            {
                From = sourceStatus.ToString(),
                To = targetStatus.ToString(),
                Reason = reason,
            }),
            operation.CorrelationId,
            clock.UtcNow));

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalized = reason.Trim();
        return normalized.Length is >= 5 and <= Order.AttentionReasonMaxLength
            ? normalized
            : null;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static OrderLifecycleMutationResult Invalid(string message) =>
        OrderLifecycleMutationResult.Failure(
            OrderLifecycleMutationStatus.Invalid,
            message);
}
