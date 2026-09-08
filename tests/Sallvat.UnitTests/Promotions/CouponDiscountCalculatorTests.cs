using Sallvat.Application.Promotions;
using Sallvat.Domain.Promotions;

namespace Sallvat.UnitTests.Promotions;

public sealed class CouponDiscountCalculatorTests
{
    [Fact]
    public void PercentageIsRoundedAndAllocatedToTheCent()
    {
        var quote = CouponDiscountCalculator.Calculate(
            CouponDiscountType.Percentage,
            10,
            [
                new CouponDiscountLine(3, 33.33m),
                new CouponDiscountLine(1, 33.33m),
                new CouponDiscountLine(2, 33.34m),
            ]);

        Assert.Equal(100m, quote.Subtotal);
        Assert.Equal(10m, quote.DiscountTotal);
        Assert.Equal(90m, quote.Total);
        Assert.Equal(quote.DiscountTotal,
            quote.Allocations.Sum(allocation => allocation.Amount));
        Assert.Equal(3.33m,
            quote.Allocations.Single(item => item.LineId == 1).Amount);
        Assert.Equal(3.34m,
            quote.Allocations.Single(item => item.LineId == 2).Amount);
    }

    [Fact]
    public void FixedDiscountNeverMakesTheOrderNegative()
    {
        var quote = CouponDiscountCalculator.Calculate(
            CouponDiscountType.FixedAmount,
            500,
            [new CouponDiscountLine(1, 49.90m)]);

        Assert.Equal(49.90m, quote.DiscountTotal);
        Assert.Equal(0m, quote.Total);
        Assert.Equal(49.90m, Assert.Single(quote.Allocations).Amount);
    }
}
