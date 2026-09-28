using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Authorization;
using Sallvat.Application.Payments;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
[Route("Admin/Pagamentos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PaymentsController(IAdminPaymentQuery payments) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(AdminPaymentFilter filter = AdminPaymentFilter.Attention,
        long? beforeId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(filter) || beforeId is <= 0)
        {
            return BadRequest();
        }

        return View(await payments.ListAsync(filter, beforeId, cancellationToken));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(id, cancellationToken);
        return payment is null ? NotFound() : View(payment);
    }
}
