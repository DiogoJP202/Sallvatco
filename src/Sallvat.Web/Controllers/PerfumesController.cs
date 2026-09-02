using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Catalog;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("perfumes")]
public sealed class PerfumesController(ICatalogService catalogService) :
    Controller
{
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

        return View(catalog);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(
        string slug,
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

        return result.Product is null
            ? NotFound()
            : View(result.Product);
    }
}
