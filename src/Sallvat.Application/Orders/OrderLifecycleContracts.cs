using Sallvat.Domain.Orders;

namespace Sallvat.Application.Orders;

public sealed record OrderAdminContext(
    Guid ActorUserId,
    string CorrelationId);

public sealed record AdminOrderSummary(
    long Id,
    string OrderNumber,
    OrderStatus Status,
    string BuyerName,
    decimal GrandTotal,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? AttentionReason,
    DateTimeOffset? AttentionSinceUtc,
    Guid ConcurrencyVersion)
{
    public bool CanFlagForAttention =>
        Order.CanTransition(Status, OrderStatus.RequiresAttention);

    public bool CanCancel =>
        Order.CanTransition(Status, OrderStatus.Cancelled);
}

public enum OrderLifecycleMutationStatus
{
    Succeeded,
    NotFound,
    Invalid,
    ConcurrencyConflict,
}

public sealed record OrderLifecycleMutationResult(
    OrderLifecycleMutationStatus Status,
    bool WasAlreadyApplied,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == OrderLifecycleMutationStatus.Succeeded;

    public static OrderLifecycleMutationResult Success(
        bool wasAlreadyApplied = false) =>
        new(
            OrderLifecycleMutationStatus.Succeeded,
            wasAlreadyApplied,
            []);

    public static OrderLifecycleMutationResult Failure(
        OrderLifecycleMutationStatus status,
        params string[] errors) =>
        new(status, false, errors);
}
