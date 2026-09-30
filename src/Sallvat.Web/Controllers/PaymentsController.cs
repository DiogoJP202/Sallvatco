using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sallvat.Application.Checkout;
using Sallvat.Application.Payments;
using Sallvat.Web.Carts;
using Sallvat.Web.Payments;
using Sallvat.Web.Security;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("pagamentos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PaymentsController(ICheckoutPaymentService payments, CartCookieManager cookies) : Controller
{
    [HttpGet("{attemptId:guid}")]
    public async Task<IActionResult> Status(Guid attemptId, CancellationToken cancellationToken)
    {
        ViewData["ForceNoIndex"] = true;
        var summary = await payments.GetAsync(attemptId, CheckoutOwner.From(HttpContext, cookies), cancellationToken);
        return summary is null ? NotFound() : View("Status", summary);
    }

    [HttpGet("retorno/{outcome}")]
    public Task<IActionResult> Return(string outcome, Guid? tentativa, CancellationToken cancellationToken)
    {
        // Route and provider query parameters express navigation, never financial authority.
        if (outcome is not ("sucesso" or "pendente" or "falha") || tentativa is null || !ModelState.IsValid)
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        return Status(tentativa.Value, cancellationToken);
    }

    [HttpPost("{attemptId:guid}/continuar")]
    [EnableRateLimiting(RateLimitPolicyNames.CheckoutPayment)]
    [RequestSizeLimit(32_768)]
    public async Task<IActionResult> Continue(Guid attemptId, bool confirmed, CancellationToken cancellationToken)
    {
        if (!payments.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!confirmed || !ModelState.IsValid)
        {
            return BadRequest();
        }

        var result = await payments.ContinueAsync(attemptId, CheckoutOwner.From(HttpContext, cookies), cancellationToken);
        if (result.Status == PaymentDispatchStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == PaymentDispatchStatus.Ready && result.CheckoutUrl is { } url)
        {
            return Redirect(url.AbsoluteUri);
        }

        TempData["PaymentMessage"] = "Não foi possível abrir o pagamento de teste. Consulte a situação do pedido antes de tentar novamente; não faça um pagamento separado.";
        return RedirectToAction(nameof(Status), new { attemptId });
    }
}
