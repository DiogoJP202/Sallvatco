using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Authorization;
using Sallvat.Application.Orders;
using Sallvat.Domain.Orders;
using Sallvat.Web.Models.Orders;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
[Route("Admin/Pedidos")]
public sealed class OrdersController(
    IOrderLifecycleService orderLifecycleService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken) =>
        View(await orderLifecycleService.ListOpenAdminAsync(
            cancellationToken));

    [HttpPost("{id:long}/Transicao")]
    public async Task<IActionResult> Transition(
        long id,
        OrderTransitionViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = string.Join(
                " ",
                ModelState.Values
                    .SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        var result = await orderLifecycleService.TransitionAsync(
            id,
            model.ConcurrencyVersion,
            model.TargetStatus,
            model.Reason,
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }
        else if (result.WasAlreadyApplied)
        {
            TempData["StatusMessage"] =
                "A transição já havia sido aplicada.";
        }
        else
        {
            TempData["StatusMessage"] = model.TargetStatus switch
            {
                OrderStatus.RequiresAttention =>
                    "Pedido enviado para revisão.",
                OrderStatus.Cancelled => "Pedido cancelado.",
                _ => "Pedido atualizado.",
            };
        }

        return RedirectToAction(nameof(Index));
    }

    private OrderAdminContext Operation()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var actorUserId))
        {
            throw new InvalidOperationException(
                "The authenticated administrator has no valid identifier.");
        }

        return new(actorUserId, HttpContext.TraceIdentifier);
    }
}
