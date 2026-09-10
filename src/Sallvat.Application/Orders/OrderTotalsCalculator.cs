namespace Sallvat.Application.Orders;

public static class OrderTotalsCalculator
{
    public static OrderTotals Calculate(
        IReadOnlyCollection<OrderAmountLine> lines,
        decimal shippingTotal)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException(
                "At least one order line is required.",
                nameof(lines));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(shippingTotal);
        if (lines.Select(line => line.LineId).Distinct().Count()
            != lines.Count)
        {
            throw new ArgumentException(
                "Order line identifiers must be unique.",
                nameof(lines));
        }

        decimal itemsSubtotal = 0;
        decimal discountTotal = 0;
        foreach (var line in lines)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                line.LineId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                line.Quantity);
            ArgumentOutOfRangeException.ThrowIfNegative(line.UnitPrice);
            ArgumentOutOfRangeException.ThrowIfNegative(
                line.DiscountAmount);
            var gross = Money(line.UnitPrice * line.Quantity);
            var discount = Money(line.DiscountAmount);
            if (discount > gross)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines),
                    "A line discount cannot exceed its gross amount.");
            }

            itemsSubtotal += gross;
            discountTotal += discount;
        }

        itemsSubtotal = Money(itemsSubtotal);
        discountTotal = Money(discountTotal);
        var shipping = Money(shippingTotal);
        return new OrderTotals(
            itemsSubtotal,
            discountTotal,
            shipping,
            Money(itemsSubtotal - discountTotal + shipping));
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
