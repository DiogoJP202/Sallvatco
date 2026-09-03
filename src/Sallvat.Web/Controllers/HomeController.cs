using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sallvat.Application.Catalog;
using Sallvat.Web.Configuration;
using Sallvat.Web.Models.Catalog;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController(
    ICatalogService catalogService,
    IOptions<AccountLinkOptions> accountLinkOptions) : Controller
{
    private readonly Uri publicOrigin = new(
        accountLinkOptions.Value.PublicOrigin,
        UriKind.Absolute);

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        ViewData["CanonicalUrl"] = new Uri(publicOrigin, "/").AbsoluteUri;
        ViewData["OpenGraphType"] = "website";

        return View(new HomePageViewModel(
            await catalogService.ListFeaturedAsync(
                3,
                cancellationToken)));
    }
}
