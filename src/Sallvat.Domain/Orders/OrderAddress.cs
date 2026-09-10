using Sallvat.Domain.Customers;

namespace Sallvat.Domain.Orders;

public sealed class OrderAddress
{
    private OrderAddress()
    {
    }

    public OrderAddress(
        long orderId,
        string recipientName,
        string postalCode,
        string street,
        string number,
        string? complement,
        string district,
        string city,
        string stateCode,
        string countryCode = "BR")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        OrderId = orderId;
        RecipientName = Required(
            recipientName,
            Address.RecipientNameMaxLength,
            nameof(recipientName));
        PostalCode = FixedDigits(
            postalCode,
            Address.PostalCodeMaxLength,
            nameof(postalCode));
        Street = Required(street, Address.StreetMaxLength, nameof(street));
        Number = Required(number, Address.NumberMaxLength, nameof(number));
        Complement = Optional(
            complement,
            Address.ComplementMaxLength,
            nameof(complement));
        District = Required(
            district,
            Address.DistrictMaxLength,
            nameof(district));
        City = Required(city, Address.CityMaxLength, nameof(city));
        StateCode = FixedCode(
            stateCode,
            Address.StateCodeMaxLength,
            nameof(stateCode));
        CountryCode = FixedCode(
            countryCode,
            Address.CountryCodeMaxLength,
            nameof(countryCode));
    }

    public long OrderId { get; private set; }

    public string RecipientName { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string Number { get; private set; } = string.Empty;

    public string? Complement { get; private set; }

    public string District { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string StateCode { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = "BR";

    private static string Required(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string? Optional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, parameterName);

    private static string FixedDigits(
        string value,
        int length,
        string parameterName)
    {
        var normalized = Required(value, length, parameterName);
        return normalized.Length == length
            && normalized.All(char.IsAsciiDigit)
                ? normalized
                : throw new ArgumentException(
                    $"Value must contain exactly {length} digits.",
                    parameterName);
    }

    private static string FixedCode(
        string value,
        int length,
        string parameterName)
    {
        var normalized = Required(value, length, parameterName)
            .ToUpperInvariant();
        return normalized.Length == length
            && normalized.All(char.IsAsciiLetter)
                ? normalized
                : throw new ArgumentException(
                    $"Value must contain exactly {length} letters.",
                    parameterName);
    }
}
