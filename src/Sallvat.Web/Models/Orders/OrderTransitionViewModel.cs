using System.ComponentModel.DataAnnotations;
using Sallvat.Domain.Orders;

namespace Sallvat.Web.Models.Orders;

public sealed class OrderTransitionViewModel
{
    public Guid ConcurrencyVersion { get; set; }

    [Required]
    public OrderStatus TargetStatus { get; set; }

    [Required(ErrorMessage = "Informe a justificativa.")]
    [StringLength(
        Order.AttentionReasonMaxLength,
        MinimumLength = 5,
        ErrorMessage = "Use entre 5 e 500 caracteres.")]
    public string Reason { get; set; } = string.Empty;
}
