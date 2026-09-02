using System.ComponentModel.DataAnnotations;

namespace Sallvat.Web.Models.Account;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(160, ErrorMessage = "Use no máximo 160 caracteres.")]
    [Display(Name = "Nome completo")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(254, ErrorMessage = "Use no máximo 254 caracteres.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Informe um telefone válido.")]
    [StringLength(32, ErrorMessage = "Use no máximo 32 caracteres.")]
    [Display(Name = "Telefone (opcional)")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Crie uma senha.")]
    [StringLength(
        128,
        MinimumLength = 12,
        ErrorMessage = "A senha deve ter entre 12 e 128 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repita a senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")]
    [Display(Name = "Confirmar senha")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(
        typeof(bool),
        "true",
        "true",
        ErrorMessage = "Você precisa aceitar os termos e a política de privacidade.")]
    public bool AcceptTerms { get; set; }
}
