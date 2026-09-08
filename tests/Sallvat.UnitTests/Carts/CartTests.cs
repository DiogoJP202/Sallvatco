using Sallvat.Domain.Carts;

namespace Sallvat.UnitTests.Carts;

public sealed class CartTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GuestCartStoresOnlyAValidatedHashAndUtcExpiration()
    {
        var hash = new string('A', Cart.GuestTokenHashLength);

        var cart = Cart.CreateGuest(hash, Now, Now.AddDays(30));

        Assert.NotEqual(Guid.Empty, cart.Id);
        Assert.Equal(hash, cart.GuestTokenHash);
        Assert.Null(cart.CustomerId);
        Assert.Equal(Now.AddDays(30), cart.ExpiresAtUtc);
        Assert.Throws<ArgumentException>(() =>
            Cart.CreateGuest("raw-token", Now, Now.AddDays(30)));
    }

    [Fact]
    public void CartItemProtectsQuantityAndCanMoveDuringMerge()
    {
        var firstCartId = Guid.NewGuid();
        var secondCartId = Guid.NewGuid();
        var item = new CartItem(
            firstCartId,
            42,
            2,
            299.90m,
            Now);

        item.ChangeQuantity(CartItem.MaximumQuantity, Now.AddMinutes(1));
        item.MoveToCart(secondCartId, Now.AddMinutes(2));

        Assert.Equal(CartItem.MaximumQuantity, item.Quantity);
        Assert.Equal(secondCartId, item.CartId);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            item.ChangeQuantity(
                CartItem.MaximumQuantity + 1,
                Now.AddMinutes(3)));
    }
}
