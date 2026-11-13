using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Catalog;
using Sallvat.Web.Models.Catalog;
using Sallvat.Web.Seo;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("perfumes")]
public sealed class PerfumesController(
    ICatalogService catalogService,
    StorefrontSeo seo) :
    Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? familia,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || pagina < 1
            || Request.Query.TryGetValue("pagina", out var pageValues)
                && (pageValues.Count != 1 || string.IsNullOrWhiteSpace(pageValues[0]))
            || Request.Query.TryGetValue("familia", out var familyValues)
                && (familyValues.Count != 1 || string.IsNullOrWhiteSpace(familyValues[0])))
        {
            return CatalogNotFound();
        }

        var catalog = await catalogService.ListPublishedAsync(
            familia,
            pagina,
            12,
            cancellationToken);

        if (pagina > catalog.TotalPages || (familia is not null && catalog.TotalItems == 0))
        {
            return CatalogNotFound();
        }

        var path = StorefrontSeo.CatalogPath(pagina, catalog.SelectedFamily);
        if (!string.Equals(Request.Path.Value, "/perfumes", StringComparison.Ordinal)
            || Request.Query.ContainsKey("pagina")
                && (pagina == 1 || pageValues[0] != pagina.ToString(CultureInfo.InvariantCulture))
            || !string.Equals(familia, catalog.SelectedFamily, StringComparison.Ordinal))
        {
            return LocalRedirectPermanent(StorefrontSeo.WithCampaign(path, Request.Query));
        }

        seo.SetPage(ViewData, path, familia is null && catalog.Items.Count > 0);

        return View(catalog);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(
        string slug,
        long? variante,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || Request.Query.TryGetValue("variante", out var variantValues)
            && (variantValues.Count != 1 || variante is null or <= 0))
        {
            return CatalogNotFound();
        }

        var result = await catalogService.FindPublishedAsync(slug, cancellationToken);
        var product = result.Product;
        if (result.RedirectSlug is { } currentSlug)
        {
            product = (await catalogService.FindPublishedAsync(currentSlug, cancellationToken)).Product;
        }

        if (product is null)
        {
            return CatalogNotFound();
        }

        var canonicalPath = StorefrontSeo.ProductPath(product.Slug);
        var selectedVariant = variante is null
            ? product.Variants.FirstOrDefault(variant => variant.Available > 0) ?? product.Variants[0]
            : product.Variants.SingleOrDefault(variant => variant.Id == variante);
        if (selectedVariant is null)
        {
            return CatalogNotFound(canonicalPath);
        }

        if (!string.Equals(Request.Path.Value, canonicalPath, StringComparison.Ordinal)
            || variante is long variantId
                && variantValues[0] != variantId.ToString(CultureInfo.InvariantCulture))
        {
            return LocalRedirectPermanent(StorefrontSeo.WithCampaign(
                StorefrontSeo.ProductPath(product.Slug, variante), Request.Query));
        }

        seo.SetPage(ViewData, canonicalPath, eligible: true, type: "product");
        var cover = product.Images.First(image => image.IsCover);
        ViewData["OpenGraphImage"] = seo.Absolute(cover.LargeUrl);
        ViewData["OpenGraphImageAlt"] = cover.AltText;
        var largeDimensions = FitWithin(
            cover.Width,
            cover.Height,
            1600,
            2000);
        ViewData["OpenGraphImageWidth"] = largeDimensions.Width;
        ViewData["OpenGraphImageHeight"] = largeDimensions.Height;

        var related = await catalogService.ListRelatedAsync(product.Id, product.OlfactoryFamily, cancellationToken);
        return View(new ProductDetailsPageViewModel(
            product,
            selectedVariant,
            related,
            ProductStructuredData.Build(product, seo)));
    }

    private ViewResult CatalogNotFound(string? productPath = null)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["ProductPath"] = productPath;
        return View("NotFound");
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
