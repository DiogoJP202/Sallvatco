using System.Security.Claims;
using Sallvat.Application.Carts;
using Sallvat.Web.Carts;

namespace Sallvat.Web.Payments;

internal static class CheckoutOwner
{
    internal static CartOwner From(HttpContext context, CartCookieManager cookies)
    {
        if (context.User.Identity?.IsAuthenticated is true)
        {
            return Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
                ? CartOwner.ForCustomer(id) : new(null, null);
        }

        var token = cookies.ReadToken(context);
        return token is null ? new(null, null) : CartOwner.ForGuest(token);
    }
}
