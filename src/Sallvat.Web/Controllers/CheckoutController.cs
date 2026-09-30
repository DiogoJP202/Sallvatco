using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Time;
using Sallvat.Web.Carts;
using Sallvat.Web.Models.Checkout;
using Sallvat.Web.Payments;
using Sallvat.Web.Security;

namespace Sallvat.Web.Controllers;

[AllowAnonymous]
[Route("checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(
    ICheckoutService checkoutService,
    ICartService cartService,
    CartCookieManager cookieManager,
    ICheckoutPaymentService payments,
    CheckoutReviewProtector reviews,
    IClock clock) : Controller
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
        var confirmations = new Dictionary<string, string>();
        if (payments.Enabled && freight.Succeeded)
        {
            var attemptId = Guid.NewGuid();
            var draft = result.Draft;
            var input = new CheckoutDraftInput(new(draft.Buyer.Name, draft.Buyer.Email, draft.Buyer.Phone),
                new(null, draft.Delivery.RecipientName, draft.Delivery.PostalCode, draft.Delivery.Street,
                    draft.Delivery.Number, draft.Delivery.Complement, draft.Delivery.District, draft.Delivery.City, draft.Delivery.StateCode));
            var expected = new OrderReviewExpectation(result.Cart.Items.Select(item =>
                new OrderReviewLine(item.VariantId, item.Quantity, item.UnitPrice, item.Currency)).ToArray(),
                result.Cart.Subtotal, result.Cart.DiscountTotal, result.Cart.Coupon?.Id);
            foreach (var option in freight.Options.Where(option => option.ExpiresAtUtc > clock.UtcNow))
            {
                var expires = clock.UtcNow.AddMinutes(10);
                confirmations[option.QuoteId] = reviews.Protect(new(attemptId, input, option, expected,
                    option.ExpiresAtUtc < expires ? option.ExpiresAtUtc : expires), owner);
            }
        }

        return View(new CheckoutReviewViewModel(
            result.Draft,
            result.Cart,
            freight)
        { Confirmations = confirmations });
    }

    [HttpPost("confirmar")]
    [EnableRateLimiting(RateLimitPolicyNames.CheckoutPayment)]
    [RequestSizeLimit(32_768)]
    public async Task<IActionResult> Confirm(string? reviewToken, bool confirmed, CancellationToken cancellationToken)
    {
        if (!payments.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var owner = CurrentOwner();
        var confirmation = reviews.Read(reviewToken, owner);
        if (!confirmed || confirmation is null || !ModelState.IsValid)
        {
            return BadRequest();
        }

        var result = await payments.ConfirmAsync(confirmation, owner, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["CheckoutError"] = string.Join(" ", result.Errors);
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction("Status", "Payments", new { attemptId = confirmation.AttemptId });
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
