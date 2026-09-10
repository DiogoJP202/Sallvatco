namespace Sallvat.Application.Orders;

public interface IOrderService
{
    Task<OrderCreationResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);
}
