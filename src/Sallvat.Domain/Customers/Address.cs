namespace Sallvat.Domain.Customers;

public sealed class Address
{
    public const int LabelMaxLength = 40;
    public const int RecipientNameMaxLength = 160;
    public const int PostalCodeMaxLength = 8;
    public const int StreetMaxLength = 180;
    public const int NumberMaxLength = 30;
    public const int ComplementMaxLength = 120;
    public const int DistrictMaxLength = 120;
    public const int CityMaxLength = 120;
    public const int StateCodeMaxLength = 2;
    public const int CountryCodeMaxLength = 2;

    private Address()
    {
    }

    public Address(
        long customerId,
        string label,
        string recipientName,
        string postalCode,
        string street,
        string number,
        string? complement,
        string district,
        string city,
        string stateCode,
        DateTimeOffset createdAtUtc,
        string countryCode = "BR")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);

        CustomerId = customerId;
        Label = Required(label, LabelMaxLength, nameof(label));
        RecipientName = Required(
            recipientName,
            RecipientNameMaxLength,
            nameof(recipientName));
        PostalCode = Digits(
            postalCode,
            PostalCodeMaxLength,
            nameof(postalCode));
        Street = Required(street, StreetMaxLength, nameof(street));
        Number = Required(number, NumberMaxLength, nameof(number));
        Complement = Optional(
            complement,
            ComplementMaxLength,
            nameof(complement));
        District = Required(district, DistrictMaxLength, nameof(district));
        City = Required(city, CityMaxLength, nameof(city));
        StateCode = FixedCode(
            stateCode,
            StateCodeMaxLength,
            nameof(stateCode));
        CountryCode = FixedCode(
            countryCode,
            CountryCodeMaxLength,
            nameof(countryCode));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public long Id { get; private set; }

    public long CustomerId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string Number { get; private set; } = string.Empty;

    public string? Complement { get; private set; }

    public string District { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string StateCode { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = "BR";

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private static string Required(
        string value,
        int maxLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }

    private static string? Optional(
        string? value,
        int maxLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maxLength, parameterName);

    private static string Digits(
        string value,
        int length,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (normalized.Length != length
            || normalized.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                $"Value must contain exactly {length} digits.",
                parameterName);
        }

        return normalized;
    }

    private static string FixedCode(
        string value,
        int length,
        string parameterName)
    {
        var normalized = Required(value, length, parameterName)
            .ToUpperInvariant();

        if (normalized.Length != length
            || normalized.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException(
                $"Value must contain exactly {length} ASCII letters.",
                parameterName);
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
