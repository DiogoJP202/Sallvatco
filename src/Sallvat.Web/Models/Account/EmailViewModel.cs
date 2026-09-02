using System.ComponentModel.DataAnnotations;

namespace Sallvat.Web.Models.Account;

public sealed class EmailViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(254, ErrorMessage = "Use no máximo 254 caracteres.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;
}
