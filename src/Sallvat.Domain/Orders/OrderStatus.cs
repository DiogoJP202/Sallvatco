namespace Sallvat.Domain.Orders;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Preparing,
    Shipped,
    Delivered,
    Cancelled,
    Refunded,
    RequiresAttention,
}
