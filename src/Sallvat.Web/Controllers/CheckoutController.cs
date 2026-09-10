using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Web.Carts;
using Sallvat.Web.Models.Checkout;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("checkout")]
public sealed class CheckoutController(
    ICheckoutService checkoutService,
    ICartService cartService,
    CartCookieManager cookieManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var owner = CurrentOwner();
        var cart = await cartService.GetAsync(owner, cancellationToken);
        if (!cart.CanStartCheckout)
        {
            TempData["CartError"] = cart.Items.Count == 0
                ? "Adicione uma fragrância antes de iniciar o checkout."
                : "Revise a disponibilidade dos itens e do cupom antes de continuar.";
            return RedirectToAction("Index", "Cart");
        }

        var prefill = await checkoutService.GetPrefillAsync(
            CurrentUserId(),
            cancellationToken);
        return View(new CheckoutPageViewModel(
            CheckoutFormViewModel.From(prefill),
            cart,
            prefill.Addresses));
    }

    [HttpPost("revisar")]
    public async Task<IActionResult> Review(
        [Bind(Prefix = "Form")] CheckoutFormViewModel form,
        CancellationToken cancellationToken)
    {
        var owner = CurrentOwner();
        var userId = CurrentUserId();
        var result = await checkoutService.ValidateAsync(
            owner,
            userId,
            form.ToInput(),
            cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    string.IsNullOrEmpty(error.Field)
                        ? string.Empty
                        : $"Form.{error.Field}",
                    error.Message);
            }
        }

        if (!ModelState.IsValid || result.Draft is null)
        {
            var prefill = await checkoutService.GetPrefillAsync(
                userId,
                cancellationToken);
            return View("Index", new CheckoutPageViewModel(
                form,
                result.Cart,
                prefill.Addresses));
        }

        var freight = await checkoutService.QuoteFreightAsync(
            owner,
            result.Draft.Delivery.PostalCode,
            cancellationToken);
        return View(new CheckoutReviewViewModel(
            result.Draft,
            result.Cart,
            freight));
    }

    private CartOwner CurrentOwner()
    {
        var userId = CurrentUserId();
        if (userId.HasValue)
        {
            return CartOwner.ForCustomer(userId.Value);
        }

        var token = cookieManager.ReadToken(HttpContext);
        return token is null
            ? new CartOwner(null, null)
            : CartOwner.ForGuest(token);
    }

    private Guid? CurrentUserId()
    {
        if (User.Identity?.IsAuthenticated is not true)
        {
            return null;
        }

        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException(
                "Authenticated user has no valid identifier.");
    }
}
