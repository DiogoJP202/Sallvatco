using Sallvat.Domain.Promotions;

namespace Sallvat.UnitTests.Promotions;

public sealed class CouponTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CouponNormalizesCodeAndEnforcesWindowAndValues()
    {
        var coupon = Create("  inverno-10 ");

        Assert.Equal("INVERNO-10", coupon.Code);
        Assert.Equal(CouponEligibilityStatus.Eligible,
            coupon.Evaluate(100m, 0, Now));
        Assert.Throws<ArgumentException>(() => Create("inválido"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coupon(
            "EXCESSO",
            CouponDiscountType.Percentage,
            101,
            0,
            null,
            null,
            null,
            null,
            true,
            Now));
        Assert.Throws<ArgumentException>(() => new Coupon(
            "JANELA",
            CouponDiscountType.FixedAmount,
            10,
            0,
            Now.AddDays(2),
            Now.AddDays(1),
            null,
            null,
            true,
            Now));
    }

    [Fact]
    public void EligibilityExplainsMinimumExpirationAndUsageLimits()
    {
        var coupon = new Coupon(
            "LIMITE10",
            CouponDiscountType.Percentage,
            10,
            100,
            Now.AddHours(-1),
            Now.AddHours(1),
            1,
            1,
            true,
            Now.AddHours(-2));

        Assert.Equal(CouponEligibilityStatus.MinimumSubtotalNotMet,
            coupon.Evaluate(99.99m, 0, Now));
        Assert.Equal(CouponEligibilityStatus.IdentityLimitReached,
            coupon.Evaluate(100m, 1, Now));
        coupon.ClaimUsage(Now);
        Assert.Equal(CouponEligibilityStatus.GlobalLimitReached,
            coupon.Evaluate(100m, 0, Now));
        coupon.ReleaseUsage(Now.AddMinutes(1));
        Assert.Equal(CouponEligibilityStatus.Expired,
            coupon.Evaluate(100m, 0, Now.AddHours(1)));
    }

    [Fact]
    public void RedemptionIsIdempotentAndRejectsLateConsumption()
    {
        var key = Guid.NewGuid();
        var redemption = new CouponRedemption(
            1,
            key,
            null,
            "cliente@example.com",
            10,
            Now,
            Now.AddMinutes(15));

        Assert.True(redemption.Consume(42, Now.AddMinutes(1)));
        Assert.False(redemption.Consume(42, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            redemption.Release(Now.AddMinutes(3)));

        var late = new CouponRedemption(
            1,
            Guid.NewGuid(),
            null,
            "cliente@example.com",
            10,
            Now,
            Now.AddMinutes(15));
        Assert.Throws<InvalidOperationException>(() =>
            late.Consume(43, Now.AddMinutes(15)));
    }

    [Fact]
    public void ConsumedRedemptionCanBeReleasedForItsOrderOnlyOnce()
    {
        var redemption = new CouponRedemption(
            1,
            Guid.NewGuid(),
            null,
            "cliente@example.com",
            10,
            Now,
            Now.AddMinutes(15));
        redemption.Consume(42, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            redemption.ReleaseForOrder(43, Now.AddMinutes(2)));
        Assert.True(redemption.ReleaseForOrder(42, Now.AddMinutes(2)));
        Assert.Equal(CouponRedemptionStatus.Released, redemption.Status);
        Assert.Equal(42, redemption.OrderId);
        Assert.False(redemption.ReleaseForOrder(42, Now.AddMinutes(3)));
    }

    private static Coupon Create(string code) => new(
        code,
        CouponDiscountType.Percentage,
        10,
        0,
        null,
        null,
        null,
        null,
        true,
        Now);
}
