using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Carts;
using Sallvat.Application.Catalog;
using Sallvat.Application.Promotions;
using Sallvat.Domain.Promotions;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Promotions;

public sealed class CartCouponTests
{
    private const string GuestToken =
        "coupon_guest_123456789012345678901234567890";

    [Fact]
    public async Task CartAppliesOneCouponAndRecalculatesItFromCurrentPrices()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "coupon-cart");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        var couponService = scope.ServiceProvider
            .GetRequiredService<ICouponService>();
        var catalogService = scope.ServiceProvider
            .GetRequiredService<ICatalogService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await couponService.CreateAsync(
            new CouponEditorInput(
                "SACOLA10",
                CouponDiscountType.Percentage,
                10,
                100,
                null,
                null,
                null,
                null,
                true),
            new(product.ActorId, "cart-coupon"))).Succeeded);
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);

        Assert.True((await cartService.ApplyCouponAsync(
            owner,
            " sacola10 ")).Succeeded);
        var initial = await cartService.GetAsync(owner);
        Assert.Equal(29.99m, initial.DiscountTotal);
        Assert.Equal(269.91m, initial.Total);

        var productDetails = await catalogService.GetAdminAsync(
            product.ProductId);
        var variant = productDetails!.Variants.Single(item =>
            item.Id == product.AvailableVariantId);
        Assert.True((await catalogService.UpdateVariantAsync(
            product.ProductId,
            variant.Id,
            variant.ConcurrencyVersion,
            new VariantEditorInput(
                variant.Sku,
                variant.VolumeMl,
                399.90m,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                variant.IsActive),
            new(product.ActorId, "cart-coupon-price"))).Succeeded);

        var recalculated = await cartService.GetAsync(owner);
        Assert.Equal(39.99m, recalculated.DiscountTotal);
        Assert.Equal(359.91m, recalculated.Total);
        Assert.True((await cartService.RemoveCouponAsync(owner)).Succeeded);
        Assert.Null((await cartService.GetAsync(owner)).Coupon);
    }

    [Fact]
    public async Task CartKeepsAnAppliedCouponButMarksItInvalidBelowMinimum()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "coupon-minimum");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        var couponService = scope.ServiceProvider
            .GetRequiredService<ICouponService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await couponService.CreateAsync(
            new CouponEditorInput(
                "MINIMO500",
                CouponDiscountType.FixedAmount,
                50,
                500,
                null,
                null,
                null,
                null,
                true),
            new(product.ActorId, "cart-minimum"))).Succeeded);
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            2)).Succeeded);
        Assert.True((await cartService.ApplyCouponAsync(
            owner,
            "MINIMO500")).Succeeded);

        var item = Assert.Single((await cartService.GetAsync(owner)).Items);
        Assert.True((await cartService.UpdateItemAsync(
            owner,
            item.ItemId,
            1)).Succeeded);
        var summary = await cartService.GetAsync(owner);

        Assert.NotNull(summary.Coupon);
        Assert.False(summary.Coupon.IsValid);
        Assert.Equal(0m, summary.DiscountTotal);
        Assert.Contains("subtotal mínimo", summary.Coupon.Message);
    }
}
