using Sallvat.Domain.Promotions;

namespace Sallvat.Application.Promotions;

public static class CouponDiscountCalculator
{
    public static CouponDiscountQuote Calculate(
        CouponDiscountType discountType,
        decimal value,
        IReadOnlyList<CouponDiscountLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!Enum.IsDefined(discountType) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (lines.Count == 0
            || lines.Any(line => line.LineId <= 0 || line.Subtotal < 0)
            || lines.Select(line => line.LineId).Distinct().Count()
                != lines.Count)
        {
            throw new ArgumentException(
                "Discount lines must be unique and non-negative.",
                nameof(lines));
        }

        var subtotal = Money(lines.Sum(line => line.Subtotal));
        var rawDiscount = discountType switch
        {
            CouponDiscountType.Percentage when value <= 100 =>
                subtotal * value / 100m,
            CouponDiscountType.FixedAmount => value,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        var discount = Math.Min(subtotal, Money(rawDiscount));
        if (discount == 0)
        {
            return new(subtotal, 0, subtotal, []);
        }

        var allocationRows = lines
            .Where(line => line.Subtotal > 0)
            .Select(line =>
            {
                var exact = discount * line.Subtotal / subtotal;
                var floor = decimal.Floor(exact * 100m) / 100m;
                return new AllocationRow(
                    line.LineId,
                    line.Subtotal,
                    floor,
                    exact - floor);
            })
            .OrderByDescending(row => row.Remainder)
            .ThenBy(row => row.LineId)
            .ToList();
        var cents = (int)((discount - allocationRows.Sum(row => row.Amount))
            * 100m);
        for (var index = 0; index < cents; index++)
        {
            var rowIndex = index % allocationRows.Count;
            var row = allocationRows[rowIndex];
            allocationRows[rowIndex] = row with
            {
                Amount = Math.Min(row.Subtotal, row.Amount + 0.01m),
            };
        }

        var allocations = allocationRows
            .OrderBy(row => row.LineId)
            .Select(row => new CouponDiscountAllocation(
                row.LineId,
                row.Amount))
            .ToArray();
        return new(
            subtotal,
            allocations.Sum(allocation => allocation.Amount),
            subtotal - allocations.Sum(allocation => allocation.Amount),
            allocations);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record AllocationRow(
        long LineId,
        decimal Subtotal,
        decimal Amount,
        decimal Remainder);
}
