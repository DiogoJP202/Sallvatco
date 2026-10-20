using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Sallvat.Domain.Customers;

namespace Sallvat.Application.Checkout;

public static partial class CheckoutDraftValidator
{
    public const string PhonePattern = @"(?:[0-9]{2}|\([0-9]{2}\)) ?[0-9]{4,5}-?[0-9]{4}";
    public const string PostalCodePattern = "[0-9]{5}-?[0-9]{3}";
    private static readonly HashSet<string> BrazilianStateCodes =
        new(StringComparer.Ordinal)
        {
            "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO",
            "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI",
            "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
        };

    public static (CheckoutDraft? Draft, IReadOnlyList<CheckoutValidationError> Errors)
        Validate(CheckoutDraftInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<CheckoutValidationError>();
        var name = Required(
            input.Buyer.Name,
            Customer.NameMaxLength,
            "BuyerName",
            "Informe o nome do comprador.",
            errors);
        var email = NormalizeEmail(input.Buyer.Email, errors);
        var phone = NormalizePhone(input.Buyer.Phone, errors);
        var recipient = Required(
            input.Delivery.RecipientName,
            Address.RecipientNameMaxLength,
            "RecipientName",
            "Informe quem receberá o pedido.",
            errors);
        var postalCode = FixedDigits(
            input.Delivery.PostalCode,
            Address.PostalCodeMaxLength,
            "PostalCode",
            "Informe um CEP com 8 dígitos.",
            errors);
        var street = Required(
            input.Delivery.Street,
            Address.StreetMaxLength,
            "Street",
            "Informe o logradouro.",
            errors);
        var number = Required(
            input.Delivery.Number,
            Address.NumberMaxLength,
            "Number",
            "Informe o número ou use “S/N”.",
            errors);
        var complement = Optional(
            input.Delivery.Complement,
            Address.ComplementMaxLength,
            "Complement",
            errors);
        var district = Required(
            input.Delivery.District,
            Address.DistrictMaxLength,
            "District",
            "Informe o bairro.",
            errors);
        var city = Required(
            input.Delivery.City,
            Address.CityMaxLength,
            "City",
            "Informe a cidade.",
            errors);
        var stateCode = NormalizeState(input.Delivery.StateCode, errors);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (
            new CheckoutDraft(
                new CheckoutBuyer(name!, email!, phone!),
                new CheckoutDelivery(
                    recipient!,
                    postalCode!,
                    street!,
                    number!,
                    complement,
                    district!,
                    city!,
                    stateCode!)),
            errors);
    }

    private static string? NormalizeEmail(
        string value,
        List<CheckoutValidationError> errors)
    {
        var email = NormalizeWhitespace(value).ToLowerInvariant();
        if (email.Length is 0 or > Customer.EmailMaxLength
            || !new EmailAddressAttribute().IsValid(email))
        {
            errors.Add(new("Email", "Informe um e-mail válido."));
            return null;
        }

        return email;
    }

    private static string? NormalizePhone(
        string? value,
        List<CheckoutValidationError> errors)
    {
        if (!PhoneRegex().IsMatch(value?.Trim() ?? string.Empty))
        {
            errors.Add(new(
                "Phone",
                "Informe um telefone brasileiro com DDD."));
            return null;
        }

        return new string(value!.Where(char.IsAsciiDigit).ToArray());
    }

    private static string? NormalizeState(
        string value,
        List<CheckoutValidationError> errors)
    {
        var state = NormalizeWhitespace(value).ToUpperInvariant();
        if (!BrazilianStateCodes.Contains(state))
        {
            errors.Add(new("StateCode", "Selecione uma UF válida."));
            return null;
        }

        return state;
    }

    private static string? FixedDigits(
        string? value,
        int length,
        string field,
        string message,
        List<CheckoutValidationError> errors)
    {
        var digits = new string((value ?? string.Empty)
            .Where(char.IsAsciiDigit)
            .ToArray());
        if (digits.Length != length
            || !PostalCodeRegex().IsMatch(value?.Trim() ?? string.Empty))
        {
            errors.Add(new(field, message));
            return null;
        }

        return digits;
    }

    private static string? Required(
        string value,
        int maxLength,
        string field,
        string requiredMessage,
        List<CheckoutValidationError> errors)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            errors.Add(new(field, requiredMessage));
            return null;
        }

        if (normalized.Length > maxLength)
        {
            errors.Add(new(
                field,
                $"Use no máximo {maxLength} caracteres."));
            return null;
        }

        return normalized;
    }

    private static string? Optional(
        string? value,
        int maxLength,
        string field,
        List<CheckoutValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(
            value,
            maxLength,
            field,
            string.Empty,
            errors);
    }

    private static string NormalizeWhitespace(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"\A(?:" + PhonePattern + @")\z")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\A(?:" + PostalCodePattern + @")\z")]
    private static partial Regex PostalCodeRegex();
}
