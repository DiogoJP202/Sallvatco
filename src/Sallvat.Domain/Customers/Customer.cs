namespace Sallvat.Domain.Customers;

public sealed class Customer
{
    public const int NameMaxLength = 160;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 32;

    private Customer()
    {
    }

    public Customer(
        string name,
        string email,
        string? phone,
        DateTimeOffset createdAtUtc)
    {
        Name = Required(name, NameMaxLength, nameof(name));
        Email = Required(email, EmailMaxLength, nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
        Phone = Optional(phone, PhoneMaxLength, nameof(phone));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public long Id { get; private set; }

    public Guid? ApplicationUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void AssociateApplicationUser(
        Guid applicationUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (applicationUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Application user ID cannot be empty.",
                nameof(applicationUserId));
        }

        if (ApplicationUserId is not null
            && ApplicationUserId != applicationUserId)
        {
            throw new InvalidOperationException(
                "Customer is already associated with another application user.");
        }

        ApplicationUserId = applicationUserId;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static string Required(
        string value,
        int maxLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, maxLength, parameterName);
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
