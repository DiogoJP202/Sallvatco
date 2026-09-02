using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed class VariantFormViewModel
{
    public long ProductId { get; set; }

    public long Id { get; set; }

    public Guid ConcurrencyVersion { get; set; }

    [Required(ErrorMessage = "Informe o SKU.")]
    [StringLength(64)]
    [RegularExpression(
        "^[A-Za-z0-9._-]+$",
        ErrorMessage = "Use letras, números, ponto, hífen ou sublinhado.")]
    public string Sku { get; set; } = string.Empty;

    [Range(1, 5000, ErrorMessage = "Informe um volume válido.")]
    [Display(Name = "Volume (ml)")]
    public int VolumeMl { get; set; }

    [Range(typeof(decimal), "0", "999999.99", ErrorMessage = "Informe um preço válido.")]
    [Display(Name = "Preço (R$)")]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "0.001", "9999", ErrorMessage = "Informe um peso válido.")]
    [Display(Name = "Peso (kg)")]
    public decimal WeightKg { get; set; }

    [Range(typeof(decimal), "0.01", "9999", ErrorMessage = "Informe uma altura válida.")]
    [Display(Name = "Altura (cm)")]
    public decimal HeightCm { get; set; }

    [Range(typeof(decimal), "0.01", "9999", ErrorMessage = "Informe uma largura válida.")]
    [Display(Name = "Largura (cm)")]
    public decimal WidthCm { get; set; }

    [Range(typeof(decimal), "0.01", "9999", ErrorMessage = "Informe um comprimento válido.")]
    [Display(Name = "Comprimento (cm)")]
    public decimal LengthCm { get; set; }

    [Display(Name = "Variante ativa")]
    public bool IsActive { get; set; } = true;

    public VariantEditorInput ToInput() =>
        new(
            Sku,
            VolumeMl,
            Price,
            WeightKg,
            HeightCm,
            WidthCm,
            LengthCm,
            IsActive);

    public static VariantFormViewModel From(
        long productId,
        AdminVariant variant) =>
        new()
        {
            ProductId = productId,
            Id = variant.Id,
            ConcurrencyVersion = variant.ConcurrencyVersion,
            Sku = variant.Sku,
            VolumeMl = variant.VolumeMl,
            Price = variant.Price,
            WeightKg = variant.WeightKg,
            HeightCm = variant.HeightCm,
            WidthCm = variant.WidthCm,
            LengthCm = variant.LengthCm,
            IsActive = variant.IsActive,
        };
}
