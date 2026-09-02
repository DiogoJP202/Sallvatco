using System.ComponentModel.DataAnnotations;

namespace Sallvat.Web.Models.Account;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe sua senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Manter acesso neste dispositivo")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
