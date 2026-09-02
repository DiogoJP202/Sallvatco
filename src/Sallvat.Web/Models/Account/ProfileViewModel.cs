using System.ComponentModel.DataAnnotations;

namespace Sallvat.Web.Models.Account;

public sealed class ProfileViewModel
{
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(160, ErrorMessage = "Use no máximo 160 caracteres.")]
    [Display(Name = "Nome completo")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Informe um telefone válido.")]
    [StringLength(32, ErrorMessage = "Use no máximo 32 caracteres.")]
    [Display(Name = "Telefone (opcional)")]
    public string? Phone { get; set; }

    public bool EmailConfirmed { get; set; }
}
