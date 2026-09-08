namespace Sallvat.Domain.Promotions;

public enum CouponEligibilityStatus
{
    Eligible,
    Inactive,
    NotStarted,
    Expired,
    MinimumSubtotalNotMet,
    GlobalLimitReached,
    IdentityLimitReached,
}
