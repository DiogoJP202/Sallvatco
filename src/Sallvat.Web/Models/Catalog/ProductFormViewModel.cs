using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed class ProductFormViewModel
{
    public long Id { get; set; }

    public Guid ConcurrencyVersion { get; set; }

    [Required(ErrorMessage = "Informe o nome do perfume.")]
    [StringLength(160, ErrorMessage = "Use no máximo 160 caracteres.")]
    [Display(Name = "Nome")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o slug.")]
    [StringLength(180, ErrorMessage = "Use no máximo 180 caracteres.")]
    [RegularExpression(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "Use apenas letras minúsculas, números e hífens.")]
    [Display(Name = "Slug da URL")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(320)]
    [Display(Name = "Descrição curta")]
    public string? ShortDescription { get; set; }

    [StringLength(6000)]
    [Display(Name = "Descrição completa")]
    public string? Description { get; set; }

    [StringLength(100)]
    [Display(Name = "Família olfativa")]
    public string? OlfactoryFamily { get; set; }

    [StringLength(500)]
    [Display(Name = "Notas de saída")]
    public string? TopNotes { get; set; }

    [StringLength(500)]
    [Display(Name = "Notas de coração")]
    public string? HeartNotes { get; set; }

    [StringLength(500)]
    [Display(Name = "Notas de fundo")]
    public string? BaseNotes { get; set; }

    [StringLength(100)]
    [Display(Name = "Concentração")]
    public string? Concentration { get; set; }

    [StringLength(100)]
    [Display(Name = "Projeção")]
    public string? Projection { get; set; }

    [StringLength(100)]
    [Display(Name = "Fixação")]
    public string? Longevity { get; set; }

    [StringLength(500)]
    [Display(Name = "Ocasiões")]
    public string? Occasions { get; set; }

    [StringLength(100)]
    [Display(Name = "Estação")]
    public string? Season { get; set; }

    [StringLength(100)]
    [Display(Name = "Período")]
    public string? Period { get; set; }

    public ProductEditorInput ToInput() =>
        new(
            Name,
            Slug,
            ShortDescription,
            Description,
            OlfactoryFamily,
            TopNotes,
            HeartNotes,
            BaseNotes,
            Concentration,
            Projection,
            Longevity,
            Occasions,
            Season,
            Period);

    public static ProductFormViewModel From(AdminProductDetails product) =>
        new()
        {
            Id = product.Id,
            ConcurrencyVersion = product.ConcurrencyVersion,
            Name = product.Product.Name,
            Slug = product.Product.Slug,
            ShortDescription = product.Product.ShortDescription,
            Description = product.Product.Description,
            OlfactoryFamily = product.Product.OlfactoryFamily,
            TopNotes = product.Product.TopNotes,
            HeartNotes = product.Product.HeartNotes,
            BaseNotes = product.Product.BaseNotes,
            Concentration = product.Product.Concentration,
            Projection = product.Product.Projection,
            Longevity = product.Product.Longevity,
            Occasions = product.Product.Occasions,
            Season = product.Product.Season,
            Period = product.Product.Period,
        };
}
