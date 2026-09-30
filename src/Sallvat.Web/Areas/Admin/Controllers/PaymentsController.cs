using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sallvat.Application.Authorization;
using Sallvat.Application.Payments;
using Sallvat.Web.Models.Payments;
using Sallvat.Web.Security;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
[Route("Admin/Pagamentos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PaymentsController(IAdminPaymentQuery payments, IPaymentRecoveryService recovery, IPaymentRefundPreparationService refunds) : Controller
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

    [HttpPost("{id:long}/Recuperar")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.PaymentRecovery)]
    [RequestSizeLimit(8192)]
    [RequestFormLimits(ValueCountLimit = 8, ValueLengthLimit = 512)]
    public async Task<IActionResult> Recover(long id, PaymentRecoveryViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0 || model.ExpectedVersion == Guid.Empty || model.Reason is null || !model.Confirm)
        {
            return BadRequest("Confirme a operação e informe uma versão e um motivo válidos.");
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
        {
            return Forbid();
        }

        var result = await recovery.RecoverAsync(id, model.ExpectedVersion,
            new(actor, model.Reason.Value, HttpContext.TraceIdentifier), cancellationToken);
        if (result == PaymentRecoveryResult.Forbidden)
        {
            return Forbid();
        }

        if (result == PaymentRecoveryResult.NotFound)
        {
            return NotFound();
        }

        TempData["RecoveryMessage"] = PaymentLabels.RecoveryResult(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/PrepararReembolso")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.PaymentRecovery)]
    [RequestSizeLimit(8192)]
    [RequestFormLimits(ValueCountLimit = 8, ValueLengthLimit = 512)]
    public async Task<IActionResult> PrepareRefund(long id, PaymentRefundViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0 || model.ExpectedVersion == Guid.Empty || model.ExpectedOrderVersion == Guid.Empty
            || model.Reason is null || !model.Confirm)
        {
            return BadRequest("Confirme o registro local e informe versões e motivo válidos.");
        }
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) { return Forbid(); }
        var result = await refunds.PrepareAsync(id, model.ExpectedVersion, model.ExpectedOrderVersion,
            new(actor, model.Reason.Value, HttpContext.TraceIdentifier), cancellationToken);
        if (result.Status == PaymentRefundPreparationStatus.Forbidden) { return Forbid(); }
        if (result.Status == PaymentRefundPreparationStatus.NotFound) { return NotFound(); }
        TempData["RefundMessage"] = PaymentLabels.RefundPreparation(result.Status);
        return RedirectToAction(nameof(Details), new { id });
    }
}
