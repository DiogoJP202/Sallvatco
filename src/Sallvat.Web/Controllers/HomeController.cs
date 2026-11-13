using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Catalog;
using Sallvat.Web.Models.Catalog;
using Sallvat.Web.Seo;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController(
    ICatalogService catalogService,
    StorefrontSeo seo) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        if (Request.Path != "/")
        {
            return LocalRedirectPermanent(StorefrontSeo.WithCampaign("/", Request.Query));
        }

        seo.SetInstitutionalPage(ViewData, "/");
        ViewData["StructuredData"] = ProductStructuredData.BuildHome(seo);

        return View(new HomePageViewModel(
            await catalogService.ListFeaturedAsync(
                3,
                cancellationToken),
            BodySplashPresentationCatalog.All
                .Where(product => product.Featured)
                .Take(3)
                .ToArray(),
            await catalogService.ListPublicFamiliesAsync(cancellationToken)));
    }

    [HttpGet("sobre")]
    public IActionResult About()
    {
        return InstitutionalPage("/sobre");
    }

    [HttpGet("linha-corporal")]
    public IActionResult BodySplashes()
    {
        seo.SetPage(ViewData, "/linha-corporal", eligible: false);

        return View(new BodySplashCollectionPageViewModel(
            BodySplashPresentationCatalog.All));
    }

    [HttpGet("contato")]
    public IActionResult Contact()
    {
        return InstitutionalPage("/contato");
    }

    [HttpGet("entrega")]
    public IActionResult Delivery()
    {
        return InstitutionalPage("/entrega");
    }

    private IActionResult InstitutionalPage(string path)
    {
        if (!string.Equals(Request.Path.Value, path, StringComparison.Ordinal))
        {
            return LocalRedirectPermanent(StorefrontSeo.WithCampaign(path, Request.Query));
        }

        seo.SetInstitutionalPage(ViewData, path);
        return View();
    }
}
