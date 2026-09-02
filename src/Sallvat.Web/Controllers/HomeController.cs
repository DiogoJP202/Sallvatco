using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
