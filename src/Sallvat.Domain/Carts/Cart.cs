namespace Sallvat.Domain.Carts;

public sealed class Cart
{
    public const int GuestTokenHashLength = 64;

    private Cart()
    {
    }

    private Cart(
        string? guestTokenHash,
        long? customerId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(guestTokenHash) == customerId.HasValue)
        {
            throw new ArgumentException(
                "A cart must belong to either a guest or a customer.");
        }

        if (customerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(customerId));
        }

        Id = Guid.NewGuid();
        GuestTokenHash = guestTokenHash is null
            ? null
            : RequireTokenHash(guestTokenHash);
        CustomerId = customerId;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        SetExpiration(expiresAtUtc);
        ConcurrencyVersion = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string? GuestTokenHash { get; private set; }

    public long? CustomerId { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public static Cart CreateGuest(
        string guestTokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(guestTokenHash, null, createdAtUtc, expiresAtUtc);

    public static Cart CreateForCustomer(
        long customerId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(null, customerId, createdAtUtc, expiresAtUtc);

    public void Refresh(
        DateTimeOffset updatedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
        SetExpiration(expiresAtUtc);
        ConcurrencyVersion = Guid.NewGuid();
    }

    private void SetExpiration(DateTimeOffset expiresAtUtc)
    {
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (ExpiresAtUtc <= UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "Cart expiration must be later than its last update.");
        }
    }

    private static string RequireTokenHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != GuestTokenHashLength
            || normalized.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "Guest token hash must be a SHA-256 hexadecimal value.",
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
