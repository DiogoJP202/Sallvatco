using System.ComponentModel.DataAnnotations;

namespace Sallvat.Web.Models.Account;

public sealed class AddressViewModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Dê um nome ao endereço.")]
    [StringLength(40, ErrorMessage = "Use no máximo 40 caracteres.")]
    [Display(Name = "Identificação")]
    public string Label { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome de quem receberá o pedido.")]
    [StringLength(160, ErrorMessage = "Use no máximo 160 caracteres.")]
    [Display(Name = "Nome do destinatário")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o CEP.")]
    [RegularExpression(
        @"^\d{5}-?\d{3}$",
        ErrorMessage = "Informe um CEP com 8 números.")]
    [Display(Name = "CEP")]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a rua ou avenida.")]
    [StringLength(180, ErrorMessage = "Use no máximo 180 caracteres.")]
    [Display(Name = "Rua ou avenida")]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o número.")]
    [StringLength(30, ErrorMessage = "Use no máximo 30 caracteres.")]
    [Display(Name = "Número")]
    public string Number { get; set; } = string.Empty;

    [StringLength(120, ErrorMessage = "Use no máximo 120 caracteres.")]
    [Display(Name = "Complemento (opcional)")]
    public string? Complement { get; set; }

    [Required(ErrorMessage = "Informe o bairro.")]
    [StringLength(120, ErrorMessage = "Use no máximo 120 caracteres.")]
    [Display(Name = "Bairro")]
    public string District { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a cidade.")]
    [StringLength(120, ErrorMessage = "Use no máximo 120 caracteres.")]
    [Display(Name = "Cidade")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a UF.")]
    [RegularExpression(
        "^[A-Za-z]{2}$",
        ErrorMessage = "Informe a UF com duas letras.")]
    [Display(Name = "UF")]
    public string StateCode { get; set; } = string.Empty;
}
