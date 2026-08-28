using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Web.Models;

namespace Sallvat.Web.Controllers;

public sealed class ErrorController : Controller
{
    [AllowAnonymous]
    [HttpGet("/erro")]
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Index()
    {
        if (HttpContext.Features.Get<IExceptionHandlerPathFeature>() is null)
        {
            return NotFound();
        }

        Response.StatusCode = StatusCodes.Status500InternalServerError;

        return View(new ErrorViewModel(HttpContext.TraceIdentifier));
    }
}
