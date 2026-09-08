using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Authorization;
using Sallvat.Application.Promotions;
using Sallvat.Web.Models.Promotions;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
[Route("Admin/Cupons")]
public sealed class CouponsController(ICouponService couponService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken) =>
        View(await couponService.ListAdminAsync(cancellationToken));

    [HttpGet("Novo")]
    public IActionResult Create() => View(new CouponEditorViewModel());

    [HttpPost("Novo")]
    public async Task<IActionResult> Create(
        CouponEditorViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await couponService.CreateAsync(
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["StatusMessage"] = "Cupom criado.";
        return RedirectToAction(nameof(Edit), new { id = result.EntityId });
    }

    [HttpGet("{id:long}/Editar")]
    public async Task<IActionResult> Edit(
        long id,
        CancellationToken cancellationToken)
    {
        var details = await couponService.GetAdminAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        ViewData["ClaimedUsageCount"] = details.ClaimedUsageCount;
        return View(CouponEditorViewModel.From(details));
    }

    [HttpPost("{id:long}/Editar")]
    public async Task<IActionResult> Edit(
        long id,
        CouponEditorViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await couponService.UpdateAsync(
            id,
            model.ConcurrencyVersion,
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["StatusMessage"] = "Cupom atualizado.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:long}/Ativacao")]
    public async Task<IActionResult> SetActive(
        long id,
        Guid concurrencyVersion,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var result = await couponService.SetActiveAsync(
            id,
            concurrencyVersion,
            isActive,
            Operation(),
            cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] =
            result.Succeeded
                ? isActive ? "Cupom ativado." : "Cupom desativado."
                : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Edit), new { id });
    }

    private PromotionAdminContext Operation()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var actorUserId))
        {
            throw new InvalidOperationException(
                "The authenticated administrator has no valid identifier.");
        }

        return new(actorUserId, HttpContext.TraceIdentifier);
    }

    private void AddErrors(CouponMutationResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
