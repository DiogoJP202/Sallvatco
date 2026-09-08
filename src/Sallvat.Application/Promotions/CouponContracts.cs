using Sallvat.Domain.Promotions;

namespace Sallvat.Application.Promotions;

public sealed record CouponEditorInput(
    string Code,
    CouponDiscountType DiscountType,
    decimal Value,
    decimal MinimumSubtotal,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    int? TotalUsageLimit,
    int? UsageLimitPerIdentity,
    bool IsActive);

public sealed record PromotionAdminContext(
    Guid ActorUserId,
    string CorrelationId);

public sealed record AdminCouponSummary(
    long Id,
    string Code,
    CouponDiscountType DiscountType,
    decimal Value,
    decimal MinimumSubtotal,
    int ClaimedUsageCount,
    int? TotalUsageLimit,
    bool IsActive,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminCouponDetails(
    long Id,
    CouponEditorInput Coupon,
    int ClaimedUsageCount,
    Guid ConcurrencyVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum CouponMutationStatus
{
    Succeeded,
    NotFound,
    Invalid,
    Duplicate,
    Unavailable,
    ConcurrencyConflict,
}

public sealed record CouponMutationResult(
    CouponMutationStatus Status,
    long? EntityId,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == CouponMutationStatus.Succeeded;

    public static CouponMutationResult Success(long? entityId = null) =>
        new(CouponMutationStatus.Succeeded, entityId, []);

    public static CouponMutationResult Failure(
        CouponMutationStatus status,
        params string[] errors) => new(status, null, errors);
}

public sealed record CouponDiscountLine(long LineId, decimal Subtotal);

public sealed record CouponDiscountAllocation(
    long LineId,
    decimal Amount);

public sealed record CouponDiscountQuote(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Total,
    IReadOnlyList<CouponDiscountAllocation> Allocations);

public sealed record CouponReservationRequest(
    string Code,
    Guid ReservationKey,
    Guid? ApplicationUserId,
    string? Email,
    IReadOnlyList<CouponDiscountLine> Lines,
    DateTimeOffset ExpiresAtUtc);

public sealed record CouponReservation(
    long RedemptionId,
    Guid ReservationKey,
    long CouponId,
    string Code,
    decimal DiscountTotal,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<CouponDiscountAllocation> Allocations);

public sealed record CouponReservationResult(
    CouponMutationStatus Status,
    CouponReservation? Reservation,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == CouponMutationStatus.Succeeded;

    public static CouponReservationResult Success(
        CouponReservation reservation) =>
        new(CouponMutationStatus.Succeeded, reservation, []);

    public static CouponReservationResult Failure(
        CouponMutationStatus status,
        params string[] errors) => new(status, null, errors);
}
