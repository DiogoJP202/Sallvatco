namespace Sallvat.Domain.Promotions;

public sealed class Coupon
{
    private Coupon()
    {
    }

    public Coupon(
        string code,
        CouponDiscountType discountType,
        decimal value,
        decimal minimumSubtotal,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? expiresAtUtc,
        int? totalUsageLimit,
        int? usageLimitPerIdentity,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        ApplyRules(
            code,
            discountType,
            value,
            minimumSubtotal,
            startsAtUtc,
            expiresAtUtc,
            totalUsageLimit,
            usageLimitPerIdentity,
            isActive);
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string NormalizedCode { get; private set; } = string.Empty;

    public CouponDiscountType DiscountType { get; private set; }

    public decimal Value { get; private set; }

    public decimal MinimumSubtotal { get; private set; }

    public DateTimeOffset? StartsAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public int? TotalUsageLimit { get; private set; }

    public int? UsageLimitPerIdentity { get; private set; }

    public int ClaimedUsageCount { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public void Update(
        string code,
        CouponDiscountType discountType,
        decimal value,
        decimal minimumSubtotal,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? expiresAtUtc,
        int? totalUsageLimit,
        int? usageLimitPerIdentity,
        bool isActive,
        DateTimeOffset updatedAtUtc)
    {
        if (totalUsageLimit.HasValue
            && ClaimedUsageCount > totalUsageLimit.Value)
        {
            throw new InvalidOperationException(
                "Total usage limit cannot be lower than the claimed usage count.");
        }

        ApplyRules(
            code,
            discountType,
            value,
            minimumSubtotal,
            startsAtUtc,
            expiresAtUtc,
            totalUsageLimit,
            usageLimitPerIdentity,
            isActive);
        Touch(updatedAtUtc);
    }

    public void SetActive(bool isActive, DateTimeOffset updatedAtUtc)
    {
        IsActive = isActive;
        Touch(updatedAtUtc);
    }

    public CouponEligibilityStatus Evaluate(
        decimal subtotal,
        int identityUsageCount,
        DateTimeOffset nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subtotal);
        ArgumentOutOfRangeException.ThrowIfNegative(identityUsageCount);
        RequireUtc(nowUtc, nameof(nowUtc));

        if (!IsActive)
        {
            return CouponEligibilityStatus.Inactive;
        }

        if (StartsAtUtc.HasValue && nowUtc < StartsAtUtc.Value)
        {
            return CouponEligibilityStatus.NotStarted;
        }

        if (ExpiresAtUtc.HasValue && nowUtc >= ExpiresAtUtc.Value)
        {
            return CouponEligibilityStatus.Expired;
        }

        if (subtotal < MinimumSubtotal)
        {
            return CouponEligibilityStatus.MinimumSubtotalNotMet;
        }

        if (TotalUsageLimit.HasValue
            && ClaimedUsageCount >= TotalUsageLimit.Value)
        {
            return CouponEligibilityStatus.GlobalLimitReached;
        }

        if (UsageLimitPerIdentity.HasValue
            && identityUsageCount >= UsageLimitPerIdentity.Value)
        {
            return CouponEligibilityStatus.IdentityLimitReached;
        }

        return CouponEligibilityStatus.Eligible;
    }

    public void ClaimUsage(DateTimeOffset updatedAtUtc)
    {
        if (TotalUsageLimit.HasValue
            && ClaimedUsageCount >= TotalUsageLimit.Value)
        {
            throw new InvalidOperationException(
                "Coupon usage limit has been reached.");
        }

        ClaimedUsageCount++;
        Touch(updatedAtUtc);
    }

    public void ReleaseUsage(DateTimeOffset updatedAtUtc)
    {
        if (ClaimedUsageCount <= 0)
        {
            throw new InvalidOperationException(
                "Coupon has no claimed usage to release.");
        }

        ClaimedUsageCount--;
        Touch(updatedAtUtc);
    }

    private void ApplyRules(
        string code,
        CouponDiscountType discountType,
        decimal value,
        decimal minimumSubtotal,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? expiresAtUtc,
        int? totalUsageLimit,
        int? usageLimitPerIdentity,
        bool isActive)
    {
        var normalizedCode = CouponCode.Normalize(code);
        if (!Enum.IsDefined(discountType))
        {
            throw new ArgumentOutOfRangeException(nameof(discountType));
        }

        if (value <= 0
            || discountType == CouponDiscountType.Percentage && value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minimumSubtotal);
        ValidateOptionalLimit(totalUsageLimit, nameof(totalUsageLimit));
        ValidateOptionalLimit(
            usageLimitPerIdentity,
            nameof(usageLimitPerIdentity));
        if (startsAtUtc.HasValue)
        {
            RequireUtc(startsAtUtc.Value, nameof(startsAtUtc));
        }

        if (expiresAtUtc.HasValue)
        {
            RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
        }

        if (startsAtUtc.HasValue
            && expiresAtUtc.HasValue
            && expiresAtUtc.Value <= startsAtUtc.Value)
        {
            throw new ArgumentException(
                "Coupon expiration must be later than its start.",
                nameof(expiresAtUtc));
        }

        Code = normalizedCode;
        NormalizedCode = normalizedCode;
        DiscountType = discountType;
        Value = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        MinimumSubtotal = decimal.Round(
            minimumSubtotal,
            2,
            MidpointRounding.AwayFromZero);
        StartsAtUtc = startsAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        TotalUsageLimit = totalUsageLimit;
        UsageLimitPerIdentity = usageLimitPerIdentity;
        IsActive = isActive;
    }

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = RequireUtc(timestamp, nameof(timestamp));
        ConcurrencyVersion = Guid.NewGuid();
    }

    private static void ValidateOptionalLimit(int? value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                parameterName);
        }

        return value;
    }
}
