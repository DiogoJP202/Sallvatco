namespace Sallvat.Domain.Promotions;

public sealed class CouponRedemption
{
    public const int NormalizedEmailMaxLength = 254;

    private CouponRedemption()
    {
    }

    public CouponRedemption(
        long couponId,
        Guid reservationKey,
        long? customerId,
        string? normalizedEmail,
        decimal discountAmount,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(couponId);
        if (reservationKey == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation key cannot be empty.",
                nameof(reservationKey));
        }

        if (customerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(customerId));
        }

        var email = NormalizeEmail(normalizedEmail);
        if (!customerId.HasValue && email is null)
        {
            throw new ArgumentException(
                "A redemption must identify a customer or e-mail.",
                nameof(normalizedEmail));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discountAmount);
        CouponId = couponId;
        ReservationKey = reservationKey;
        CustomerId = customerId;
        NormalizedEmail = email;
        DiscountAmount = decimal.Round(
            discountAmount,
            2,
            MidpointRounding.AwayFromZero);
        ReservedAtUtc = RequireUtc(reservedAtUtc, nameof(reservedAtUtc));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (ExpiresAtUtc <= ReservedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        UpdatedAtUtc = ReservedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public long CouponId { get; private set; }

    public Guid ReservationKey { get; private set; }

    public long? OrderId { get; private set; }

    public long? CustomerId { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public CouponRedemptionStatus Status { get; private set; } =
        CouponRedemptionStatus.Reserved;

    public decimal DiscountAmount { get; private set; }

    public DateTimeOffset ReservedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public bool Consume(long orderId, DateTimeOffset consumedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        if (Status == CouponRedemptionStatus.Consumed)
        {
            if (OrderId != orderId)
            {
                throw new InvalidOperationException(
                    "Redemption was consumed by another order.");
            }

            return false;
        }

        if (Status != CouponRedemptionStatus.Reserved)
        {
            throw new InvalidOperationException(
                "Only a reserved redemption can be consumed.");
        }

        var timestamp = RequireUtc(consumedAtUtc, nameof(consumedAtUtc));
        if (timestamp < ReservedAtUtc || timestamp >= ExpiresAtUtc)
        {
            throw new InvalidOperationException(
                "Expired redemption cannot be consumed.");
        }

        OrderId = orderId;
        Status = CouponRedemptionStatus.Consumed;
        ConsumedAtUtc = timestamp;
        Touch(timestamp);
        return true;
    }

    public bool Release(DateTimeOffset releasedAtUtc)
    {
        if (Status == CouponRedemptionStatus.Released)
        {
            return false;
        }

        if (Status != CouponRedemptionStatus.Reserved)
        {
            throw new InvalidOperationException(
                "Consumed redemption cannot be released automatically.");
        }

        var timestamp = RequireUtc(releasedAtUtc, nameof(releasedAtUtc));
        if (timestamp < ReservedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(releasedAtUtc));
        }

        Status = CouponRedemptionStatus.Released;
        ReleasedAtUtc = timestamp;
        Touch(timestamp);
        return true;
    }

    public bool ReleaseForOrder(
        long orderId,
        DateTimeOffset releasedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        if (Status == CouponRedemptionStatus.Released)
        {
            if (OrderId != orderId)
            {
                throw new InvalidOperationException(
                    "Redemption belongs to another order.");
            }

            return false;
        }

        if (Status != CouponRedemptionStatus.Consumed
            || OrderId != orderId)
        {
            throw new InvalidOperationException(
                "Only this order's consumed redemption can be released.");
        }

        var timestamp = RequireUtc(releasedAtUtc, nameof(releasedAtUtc));
        if (ConsumedAtUtc is not DateTimeOffset consumedAtUtc
            || timestamp < consumedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(releasedAtUtc));
        }

        Status = CouponRedemptionStatus.Released;
        ReleasedAtUtc = timestamp;
        Touch(timestamp);
        return true;
    }

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = timestamp;
        ConcurrencyVersion = Guid.NewGuid();
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > NormalizedEmailMaxLength
            || !normalized.Contains('@'))
        {
            throw new ArgumentException(
                "Normalized e-mail is invalid.",
                nameof(value));
        }

        return normalized;
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
