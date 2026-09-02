using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Authorization;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
