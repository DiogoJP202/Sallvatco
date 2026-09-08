using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Carts;

namespace Sallvat.Web.Models.Cart;

public sealed record CartPageViewModel(CartSummary Cart);

public sealed class AddCartItemViewModel
{
    [Range(1, long.MaxValue)]
    public long VariantId { get; set; }

    [Range(1, CartLimits.MaximumQuantityPerItem)]
    public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartItemViewModel
{
    [Range(1, CartLimits.MaximumQuantityPerItem)]
    public int Quantity { get; set; }
}
