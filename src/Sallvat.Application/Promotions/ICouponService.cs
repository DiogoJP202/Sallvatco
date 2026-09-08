namespace Sallvat.Application.Promotions;

public interface ICouponService
{
    Task<IReadOnlyList<AdminCouponSummary>> ListAdminAsync(
        CancellationToken cancellationToken = default);

    Task<AdminCouponDetails?> GetAdminAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<CouponMutationResult> CreateAsync(
        CouponEditorInput input,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default);

    Task<CouponMutationResult> UpdateAsync(
        long id,
        Guid concurrencyVersion,
        CouponEditorInput input,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default);

    Task<CouponMutationResult> SetActiveAsync(
        long id,
        Guid concurrencyVersion,
        bool isActive,
        PromotionAdminContext operation,
        CancellationToken cancellationToken = default);

    Task<CouponReservationResult> ReserveAsync(
        CouponReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<CouponMutationResult> ConsumeAsync(
        Guid reservationKey,
        long orderId,
        CancellationToken cancellationToken = default);

    Task<CouponMutationResult> ReleaseAsync(
        Guid reservationKey,
        CancellationToken cancellationToken = default);

    Task<int> ReleaseExpiredAsync(
        int maximumItems,
        CancellationToken cancellationToken = default);
}
