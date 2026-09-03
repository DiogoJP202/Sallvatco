using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sallvat.Application.Catalog;
using Sallvat.Web.Configuration;
using Sallvat.Web.Models.Catalog;
using Sallvat.Web.Seo;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("perfumes")]
public sealed class PerfumesController(
    ICatalogService catalogService,
    IOptions<AccountLinkOptions> accountLinkOptions) :
    Controller
{
    private readonly Uri publicOrigin = new(
        accountLinkOptions.Value.PublicOrigin,
        UriKind.Absolute);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? familia,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        var catalog = await catalogService.ListPublishedAsync(
            familia,
            pagina,
            12,
            cancellationToken);

        ViewData["CanonicalUrl"] = new Uri(
            publicOrigin,
            "/perfumes").AbsoluteUri;
        ViewData["OpenGraphType"] = "website";

        return View(catalog);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(
        string slug,
        long? variante,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.FindPublishedAsync(
            slug,
            cancellationToken);
        if (result.RedirectSlug is not null)
        {
            return RedirectToActionPermanent(
                nameof(Details),
                new { slug = result.RedirectSlug });
        }

        if (result.Product is null)
        {
            return NotFound();
        }

        var selectedVariant = result.Product.Variants.SingleOrDefault(
            variant => variant.Id == variante)
            ?? result.Product.Variants.FirstOrDefault(
                variant => variant.Available > 0)
            ?? result.Product.Variants[0];
        var canonicalUrl = new Uri(
            publicOrigin,
            $"/perfumes/{result.Product.Slug}");
        var cover = result.Product.Images.First(image => image.IsCover);
        ViewData["CanonicalUrl"] = canonicalUrl.AbsoluteUri;
        ViewData["OpenGraphType"] = "product";
        ViewData["OpenGraphImage"] = new Uri(
            publicOrigin,
            cover.LargeUrl).AbsoluteUri;
        ViewData["OpenGraphImageAlt"] = cover.AltText;
        var largeDimensions = FitWithin(
            cover.Width,
            cover.Height,
            1600,
            2000);
        ViewData["OpenGraphImageWidth"] = largeDimensions.Width;
        ViewData["OpenGraphImageHeight"] = largeDimensions.Height;

        return View(new ProductDetailsPageViewModel(
            result.Product,
            selectedVariant,
            ProductStructuredData.Build(
                result.Product,
                canonicalUrl,
                publicOrigin)));
    }

    private static (int Width, int Height) FitWithin(
        int width,
        int height,
        int maximumWidth,
        int maximumHeight)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maximumWidth / width,
                (double)maximumHeight / height));

        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}
