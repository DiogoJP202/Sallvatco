using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Shipping;
using Sallvat.Domain.Customers;

namespace Sallvat.Web.Models.Checkout;

public sealed class CheckoutFormViewModel
{
    [Required(ErrorMessage = "Informe o nome do comprador.")]
    [StringLength(Customer.NameMaxLength)]
    [Display(Name = "Nome completo")]
    public string BuyerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(Customer.EmailMaxLength)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe um telefone com DDD.")]
    [StringLength(Customer.PhoneMaxLength)]
    [Display(Name = "Telefone")]
    public string Phone { get; set; } = string.Empty;

    [Display(Name = "Endereço salvo")]
    public long? SavedAddressId { get; set; }

    [Required(ErrorMessage = "Informe quem receberá o pedido.")]
    [StringLength(Address.RecipientNameMaxLength)]
    [Display(Name = "Destinatário")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o CEP.")]
    [StringLength(9)]
    [Display(Name = "CEP")]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o logradouro.")]
    [StringLength(Address.StreetMaxLength)]
    [Display(Name = "Rua ou avenida")]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o número ou use S/N.")]
    [StringLength(Address.NumberMaxLength)]
    [Display(Name = "Número")]
    public string Number { get; set; } = string.Empty;

    [StringLength(Address.ComplementMaxLength)]
    [Display(Name = "Complemento")]
    public string? Complement { get; set; }

    [Required(ErrorMessage = "Informe o bairro.")]
    [StringLength(Address.DistrictMaxLength)]
    [Display(Name = "Bairro")]
    public string District { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a cidade.")]
    [StringLength(Address.CityMaxLength)]
    [Display(Name = "Cidade")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione a UF.")]
    [StringLength(2, MinimumLength = 2)]
    [Display(Name = "UF")]
    public string StateCode { get; set; } = string.Empty;

    public CheckoutDraftInput ToInput() => new(
        new(BuyerName, Email, Phone),
        new(
            SavedAddressId,
            RecipientName,
            PostalCode,
            Street,
            Number,
            Complement,
            District,
            City,
            StateCode));

    public static CheckoutFormViewModel From(CheckoutPrefill prefill)
    {
        var firstAddress = prefill.Addresses.Count > 0
            ? prefill.Addresses[0]
            : null;
        return new CheckoutFormViewModel
        {
            BuyerName = prefill.Name ?? string.Empty,
            Email = prefill.Email ?? string.Empty,
            Phone = prefill.Phone ?? string.Empty,
            SavedAddressId = firstAddress?.Id,
            RecipientName = firstAddress?.RecipientName
                ?? prefill.Name
                ?? string.Empty,
            PostalCode = firstAddress?.PostalCode ?? string.Empty,
            Street = firstAddress?.Street ?? string.Empty,
            Number = firstAddress?.Number ?? string.Empty,
            Complement = firstAddress?.Complement,
            District = firstAddress?.District ?? string.Empty,
            City = firstAddress?.City ?? string.Empty,
            StateCode = firstAddress?.StateCode ?? string.Empty,
        };
    }
}

public sealed record CheckoutPageViewModel(
    CheckoutFormViewModel Form,
    CartSummary Cart,
    IReadOnlyList<CheckoutSavedAddress> SavedAddresses);

public sealed record CheckoutReviewViewModel(
    CheckoutDraft Draft,
    CartSummary Cart,
    FreightQuoteResult Freight);
