using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Models.Catalog;

public sealed class ProductImageUploadViewModel
{
    public long ProductId { get; set; }

    public Guid ConcurrencyVersion { get; set; }

    [Required(ErrorMessage = "Selecione uma imagem.")]
    [Display(Name = "Arquivo")]
    public IFormFile? Image { get; set; }

    [Required(ErrorMessage = "Descreva a imagem para leitores de tela.")]
    [StringLength(200, ErrorMessage = "Use no máximo 200 caracteres.")]
    [Display(Name = "Texto alternativo")]
    public string AltText { get; set; } = string.Empty;
}

public sealed class ProductImageManagementViewModel
{
    public long ProductId { get; set; }

    public Guid ConcurrencyVersion { get; set; }

    [Required]
    public long CoverImageId { get; set; }

    public List<ProductImageItemViewModel> Images { get; set; } = [];

    public IReadOnlyList<ProductImagePresentationInput> ToInput() =>
        Images.Select(image => new ProductImagePresentationInput(
            image.ImageId,
            image.AltText,
            image.Position,
            image.ImageId == CoverImageId)).ToList();
}

public sealed class ProductImageItemViewModel
{
    public long ImageId { get; set; }

    [Required(ErrorMessage = "Informe o texto alternativo.")]
    [StringLength(200, ErrorMessage = "Use no máximo 200 caracteres.")]
    public string AltText { get; set; } = string.Empty;

    [Range(0, 9, ErrorMessage = "Use uma posição entre 0 e 9.")]
    public int Position { get; set; }
}
