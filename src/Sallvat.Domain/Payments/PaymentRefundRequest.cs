using Sallvat.Domain.Orders;

namespace Sallvat.Domain.Payments;

public enum PaymentRefundReason { CustomerRequest, FulfillmentUnavailable, OperationalCorrection }
public enum PaymentRefundRequestState { Prepared }

// A durable local intention, not proof of an external refund. Its ID is the future idempotency key.
public sealed class PaymentRefundRequest
{
    private PaymentRefundRequest() { }

    public PaymentRefundRequest(Guid id, Payment payment, Order order, Guid actorUserId,
        PaymentRefundReason reason, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(order);
        if (id == Guid.Empty || actorUserId == Guid.Empty || !Enum.IsDefined(reason))
        {
            throw new ArgumentException("A valid request, actor and reason are required.");
        }
        if (!CanPrepare(payment, order))
        {
            throw new InvalidOperationException("Only a confirmed Sandbox capture can prepare a total refund.");
        }
        if (createdAtUtc.Offset != TimeSpan.Zero || createdAtUtc < payment.UpdatedAtUtc || createdAtUtc < order.UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAtUtc));
        }
        Id = id;
        PaymentId = payment.Id;
        Amount = payment.Amount;
        Currency = payment.Currency;
        Environment = payment.Environment;
        ExternalOrderId = payment.ExternalOrderId!;
        ExternalPaymentId = payment.ExternalPaymentId!;
        PaymentVersion = payment.ConcurrencyVersion;
        OrderVersion = order.ConcurrencyVersion;
        ActorUserId = actorUserId;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public long PaymentId { get; private set; }
    public PaymentRefundRequestState State { get; private set; } = PaymentRefundRequestState.Prepared;
    public PaymentEnvironment Environment { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string ExternalOrderId { get; private set; } = string.Empty;
    public string ExternalPaymentId { get; private set; } = string.Empty;
    public Guid PaymentVersion { get; private set; }
    public Guid OrderVersion { get; private set; }
    public Guid ActorUserId { get; private set; }
    public PaymentRefundReason Reason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    public static bool CanPrepare(Payment payment, Order order) => payment.Id > 0 && payment.OrderId == order.Id
        && payment.Provider == "MercadoPago" && payment.Environment == PaymentEnvironment.Sandbox
        && payment.Status == PaymentStatus.Approved && payment.DispatchState == PaymentDispatchState.Completed
        && payment.PreferenceId is null && payment.ExternalOrderId is not null && payment.ExternalPaymentId is not null
        && payment.ConfirmedAtUtc is not null && payment.ProviderUpdatedAtUtc is not null
        && payment.Amount > 0 && payment.Amount == order.GrandTotal && payment.Currency == "BRL" && order.Currency == payment.Currency
        && payment.ExternalReference == order.OrderNumber
        && order.Status is OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered;
}
