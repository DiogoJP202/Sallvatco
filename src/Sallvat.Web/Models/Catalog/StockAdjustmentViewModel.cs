using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed class StockAdjustmentViewModel
{
    public long ProductId { get; set; }

    public long VariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int CurrentOnHand { get; set; }

    public int Reserved { get; set; }

    public Guid ConcurrencyVersion { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "O estoque não pode ser negativo.")]
    [Display(Name = "Novo estoque físico")]
    public int NewOnHand { get; set; }

    [Required(ErrorMessage = "Informe o motivo do ajuste.")]
    [StringLength(500)]
    [Display(Name = "Motivo")]
    public string Reason { get; set; } = string.Empty;

    public IReadOnlyList<InventoryMovementView> Movements { get; set; } = [];
}
