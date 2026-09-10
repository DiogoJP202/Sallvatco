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

    public bool Consume(DateTimeOffset consumedAtUtc)
    {
        if (Status == StockReservationStatus.Consumed)
        {
            return false;
        }

        if (Status != StockReservationStatus.Reserved)
        {
            throw new InvalidOperationException(
                "Only a reserved stock hold can be consumed.");
        }

        var timestamp = CompletionTimestamp(
            consumedAtUtc,
            nameof(consumedAtUtc));
        Status = StockReservationStatus.Consumed;
        ConsumedAtUtc = timestamp;
        Touch(timestamp);
        return true;
    }

    public bool Release(DateTimeOffset releasedAtUtc)
    {
        if (Status == StockReservationStatus.Released)
        {
            return false;
        }

        if (Status != StockReservationStatus.Reserved)
        {
            throw new InvalidOperationException(
                "Consumed stock cannot be released as a reservation.");
        }

        var timestamp = CompletionTimestamp(
            releasedAtUtc,
            nameof(releasedAtUtc));
        Status = StockReservationStatus.Released;
        ReleasedAtUtc = timestamp;
        Touch(timestamp);
        return true;
    }

    private DateTimeOffset CompletionTimestamp(
        DateTimeOffset value,
        string parameterName)
    {
        var timestamp = RequireUtc(value, parameterName);
        if (timestamp < ReservedAtUtc)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return timestamp;
    }

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = timestamp;
        ConcurrencyVersion = Guid.NewGuid();
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
