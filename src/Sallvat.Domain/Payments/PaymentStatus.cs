namespace Sallvat.Domain.Payments;

public enum PaymentStatus
{
    Created,
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Expired,
    Refunded,
    RequiresAttention,
}
