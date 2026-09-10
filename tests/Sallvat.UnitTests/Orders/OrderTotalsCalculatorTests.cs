using Sallvat.Application.Orders;

namespace Sallvat.UnitTests.Orders;

public sealed class OrderTotalsCalculatorTests
{
    [Fact]
    public void CalculatesServerAmountsWithMoneyRounding()
    {
        var result = OrderTotalsCalculator.Calculate(
            [
                new(1, 2, 70m, 9.99m),
                new(2, 1, 45m, 5.01m),
            ],
            18.50m);

        Assert.Equal(185m, result.ItemsSubtotal);
        Assert.Equal(15m, result.DiscountTotal);
        Assert.Equal(18.50m, result.ShippingTotal);
        Assert.Equal(188.50m, result.GrandTotal);
    }

    [Fact]
    public void RejectsDuplicateLinesAndDiscountAboveGross()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderTotalsCalculator.Calculate(
                [new(1, 1, 10m, 0m), new(1, 1, 20m, 0m)],
                5m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrderTotalsCalculator.Calculate(
                [new(1, 1, 10m, 10.01m)],
                5m));
    }
}
