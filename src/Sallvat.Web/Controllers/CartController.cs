using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Carts;
using Sallvat.Web.Carts;
using Sallvat.Web.Models.Cart;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("carrinho")]
public sealed class CartController(
    ICartService cartService,
    CartCookieManager cookieManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var summary = await cartService.GetAsync(
            CurrentOwner(createGuest: false),
            cancellationToken);

        return View(new CartPageViewModel(summary));
    }

    [HttpPost("itens")]
    public async Task<IActionResult> AddItem(
        AddCartItemViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["CartError"] =
                $"Escolha entre 1 e {CartLimits.MaximumQuantityPerItem} unidades.";
            return RedirectToAction(nameof(Index));
        }

        var result = await cartService.AddItemAsync(
            CurrentOwner(createGuest: true),
            model.VariantId,
            model.Quantity,
            cancellationToken);
        SetFeedback(
            result,
            "Fragrância adicionada à sua sacola.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("itens/{itemId:long}")]
    public async Task<IActionResult> UpdateItem(
        long itemId,
        UpdateCartItemViewModel model,
        CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await cartService.UpdateItemAsync(
                CurrentOwner(createGuest: false),
                itemId,
                model.Quantity,
                cancellationToken)
            : CartMutationResult.Failure(
                CartMutationStatus.Invalid,
                $"Escolha entre 1 e {CartLimits.MaximumQuantityPerItem} unidades.");
        SetFeedback(result, "Quantidade atualizada.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("itens/{itemId:long}/remover")]
    public async Task<IActionResult> RemoveItem(
        long itemId,
        CancellationToken cancellationToken)
    {
        var result = await cartService.RemoveItemAsync(
            CurrentOwner(createGuest: false),
            itemId,
            cancellationToken);
        SetFeedback(result, "Item removido da sacola.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("limpar")]
    public async Task<IActionResult> Clear(
        CancellationToken cancellationToken)
    {
        var result = await cartService.ClearAsync(
            CurrentOwner(createGuest: false),
            cancellationToken);
        SetFeedback(result, "Sacola esvaziada.");

        return RedirectToAction(nameof(Index));
    }

    private CartOwner CurrentOwner(bool createGuest)
    {
        if (User.Identity?.IsAuthenticated is true)
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId)
                ? CartOwner.ForCustomer(userId)
                : throw new InvalidOperationException(
                    "Authenticated user has no valid identifier.");
        }

        var token = createGuest
            ? cookieManager.GetOrCreateToken(HttpContext)
            : cookieManager.ReadToken(HttpContext);
        return token is null
            ? new CartOwner(null, null)
            : CartOwner.ForGuest(token);
    }

    private void SetFeedback(
        CartMutationResult result,
        string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["CartStatus"] = successMessage;
            return;
        }

        TempData["CartError"] = result.Errors.Count > 0
            ? result.Errors[0]
            : "Não foi possível atualizar a sacola.";
    }
}
