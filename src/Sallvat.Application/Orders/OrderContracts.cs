using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Domain.Orders;

namespace Sallvat.Application.Orders;

public sealed record CheckoutShippingSnapshot(
    string Provider,
    string Carrier,
    string Service,
    string QuoteId,
    decimal Price,
    string Currency,
    int MinimumBusinessDays,
    int MaximumBusinessDays,
    DateTimeOffset QuotedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record CreateOrderRequest(
    Guid CheckoutAttemptId,
    CartOwner CartOwner,
    CheckoutDraftInput Checkout,
    CheckoutShippingSnapshot Shipping);

public sealed record OrderAmountLine(
    long LineId,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount);

public sealed record OrderTotals(
    decimal ItemsSubtotal,
    decimal DiscountTotal,
    decimal ShippingTotal,
    decimal GrandTotal);

public sealed record CreatedOrder(
    long Id,
    string OrderNumber,
    OrderStatus Status,
    decimal GrandTotal,
    string Currency,
    DateTimeOffset ExpiresAtUtc);

public enum OrderCreationStatus
{
    Succeeded,
    Invalid,
    NotFound,
    Unavailable,
    ConcurrencyConflict,
}

public sealed record OrderCreationResult(
    OrderCreationStatus Status,
    CreatedOrder? Order,
    bool WasAlreadyCreated,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded =>
        Status == OrderCreationStatus.Succeeded && Order is not null;

    public static OrderCreationResult Success(
        CreatedOrder order,
        bool wasAlreadyCreated = false) =>
        new(
            OrderCreationStatus.Succeeded,
            order,
            wasAlreadyCreated,
            []);

    public static OrderCreationResult Failure(
        OrderCreationStatus status,
        params string[] errors) =>
        new(status, null, false, errors);
}
