namespace Sallvat.Domain.Inventory;

public sealed class StockReservation
{
    private StockReservation()
    {
    }

    public StockReservation(
        long orderId,
        long productVariantId,
        int quantity,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productVariantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        OrderId = orderId;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        ReservedAtUtc = RequireUtc(
            reservedAtUtc,
            nameof(reservedAtUtc));
        ExpiresAtUtc = RequireUtc(
            expiresAtUtc,
            nameof(expiresAtUtc));
        if (ExpiresAtUtc <= ReservedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        UpdatedAtUtc = ReservedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public long OrderId { get; private set; }

    public long ProductVariantId { get; private set; }

    public int Quantity { get; private set; }

    public StockReservationStatus Status { get; private set; } =
        StockReservationStatus.Reserved;

    public DateTimeOffset ReservedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

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
