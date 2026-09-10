using Sallvat.Domain.Orders;

namespace Sallvat.Application.Orders;

public interface IOrderLifecycleService
{
    Task<IReadOnlyList<AdminOrderSummary>> ListOpenAdminAsync(
        CancellationToken cancellationToken = default);

    Task<OrderLifecycleMutationResult> TransitionAsync(
        long orderId,
        Guid concurrencyVersion,
        OrderStatus targetStatus,
        string reason,
        OrderAdminContext operation,
        CancellationToken cancellationToken = default);

    Task<int> ExpirePendingAsync(
        int maximumItems,
        CancellationToken cancellationToken = default);
}
