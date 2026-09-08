using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Accounts;
using Sallvat.Application.Carts;
using Sallvat.Application.Catalog;
using Sallvat.Application.Time;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Carts;

public sealed class CartServiceTests
{
    private const string GuestToken =
        "guest_token_1234567890123456789012345678901";

    [Fact]
    public async Task GuestCartStoresHashAndRecalculatesPriceAndStock()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-recalculation");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        var catalogService = scope.ServiceProvider
            .GetRequiredService<ICatalogService>();
        var owner = CartOwner.ForGuest(GuestToken);

        var added = await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            2);
        var firstSummary = await cartService.GetAsync(owner);

        Assert.True(added.Succeeded);
        var firstLine = Assert.Single(firstSummary.Items);
        Assert.Equal(599.80m, firstSummary.Subtotal);
        Assert.True(firstLine.IsAvailable);
        Assert.False(firstLine.PriceChanged);

        var productDetails = await catalogService.GetAdminAsync(
            product.ProductId);
        var variant = Assert.Single(
            productDetails!.Variants,
            item => item.Id == product.AvailableVariantId);
        var updated = await catalogService.UpdateVariantAsync(
            product.ProductId,
            product.AvailableVariantId,
            variant.ConcurrencyVersion,
            new VariantEditorInput(
                variant.Sku,
                variant.VolumeMl,
                329.90m,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                variant.IsActive),
            new AdminOperationContext(
                product.ActorId,
                "cart-recalculation-test"));
        Assert.True(updated.Succeeded);

        var currentSummary = await cartService.GetAsync(owner);
        var currentLine = Assert.Single(currentSummary.Items);
        Assert.Equal(659.80m, currentSummary.Subtotal);
        Assert.True(currentLine.PriceChanged);
        Assert.Equal(299.90m, currentLine.ReferenceUnitPrice);
        Assert.Equal(329.90m, currentLine.UnitPrice);

        productDetails = await catalogService.GetAdminAsync(product.ProductId);
        variant = Assert.Single(
            productDetails!.Variants,
            item => item.Id == product.AvailableVariantId);
        var deactivated = await catalogService.UpdateVariantAsync(
            product.ProductId,
            product.AvailableVariantId,
            variant.ConcurrencyVersion,
            new VariantEditorInput(
                variant.Sku,
                variant.VolumeMl,
                variant.Price,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                false),
            new AdminOperationContext(
                product.ActorId,
                "cart-deactivation-test"));
        Assert.True(deactivated.Succeeded);
        Assert.False(Assert.Single(
            (await cartService.GetAsync(owner)).Items).IsAvailable);

        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var storedCart = Assert.Single(context.Carts);
        Assert.DoesNotContain(
            GuestToken,
            storedCart.GuestTokenHash!,
            StringComparison.Ordinal);
        Assert.Equal(64, storedCart.GuestTokenHash!.Length);
    }

    [Fact]
    public async Task QuantityCannotExceedCurrentStockOrTechnicalLimit()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-stock");
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICartService>();
        var owner = CartOwner.ForGuest(GuestToken);

        var unavailable = await service.AddItemAsync(
            owner,
            product.AvailableVariantId,
            5);
        var excessive = await service.AddItemAsync(
            owner,
            product.AvailableVariantId,
            CartLimits.MaximumQuantityPerItem + 1);

        Assert.Equal(CartMutationStatus.Unavailable, unavailable.Status);
        Assert.Contains("apenas 4 unidades", unavailable.Errors[0]);
        Assert.Equal(CartMutationStatus.Invalid, excessive.Status);
        Assert.Empty((await service.GetAsync(owner)).Items);
    }

    [Fact]
    public async Task TamperedGuestTokenCannotReadOrCreateACart()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-tampered-token");
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICartService>();
        var owner = CartOwner.ForGuest(new string('!', 43));

        var result = await service.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1);

        Assert.Equal(CartMutationStatus.Invalid, result.Status);
        Assert.Empty((await service.GetAsync(owner)).Items);
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Empty(context.Carts);
    }

    [Fact]
    public async Task GuestCanUpdateRemoveAndClearOwnedItems()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var firstProduct = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-operations-one");
        var secondProduct = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-operations-two");
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICartService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await service.AddItemAsync(
            owner,
            firstProduct.AvailableVariantId,
            1)).Succeeded);
        Assert.True((await service.AddItemAsync(
            owner,
            secondProduct.AvailableVariantId,
            1)).Succeeded);
        var firstSummary = await service.GetAsync(owner);
        var firstItem = firstSummary.Items.Single(item =>
            item.ProductSlug == "cart-operations-one");
        var secondItem = firstSummary.Items.Single(item =>
            item.ProductSlug == "cart-operations-two");

        Assert.True((await service.UpdateItemAsync(
            owner,
            firstItem.ItemId,
            3)).Succeeded);
        Assert.True((await service.RemoveItemAsync(
            owner,
            secondItem.ItemId)).Succeeded);
        Assert.Equal(3, Assert.Single(
            (await service.GetAsync(owner)).Items).Quantity);

        Assert.True((await service.ClearAsync(owner)).Succeeded);
        Assert.Empty((await service.GetAsync(owner)).Items);
    }

    [Fact]
    public async Task GuestAndCustomerCartsMergeDeterministically()
    {
        await using var application = new AccountWebApplicationFactory();
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-merge");
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        var accountService = scope.ServiceProvider
            .GetRequiredService<IAccountService>();
        var registration = await accountService.RegisterAsync(
            new RegisterAccountCommand(
                "Cliente Carrinho",
                "cart@example.com",
                null,
                "Segura#2026Perfume"));
        var challenge = Assert.IsType<AccountEmailChallenge>(
            registration.EmailChallenge);
        Assert.True(await accountService.ConfirmEmailAsync(
            challenge.UserId,
            challenge.Token));

        Assert.True((await cartService.AddItemAsync(
            CartOwner.ForGuest(GuestToken),
            product.AvailableVariantId,
            1)).Succeeded);
        Assert.True((await cartService.AddItemAsync(
            CartOwner.ForCustomer(challenge.UserId),
            product.AvailableVariantId,
            2)).Succeeded);

        await cartService.MergeGuestCartAsync(
            GuestToken,
            challenge.UserId);

        var customerCart = await cartService.GetAsync(
            CartOwner.ForCustomer(challenge.UserId));
        Assert.Equal(3, Assert.Single(customerCart.Items).Quantity);
        Assert.Empty((await cartService.GetAsync(
            CartOwner.ForGuest(GuestToken))).Items);
    }

    [Fact]
    public async Task ExpiredCartsAreHiddenAndDeletedInBatches()
    {
        var clock = new MutableClock(
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "cart-expiration");
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICartService>();
        var owner = CartOwner.ForGuest(GuestToken);
        Assert.True((await service.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);

        clock.UtcNow = clock.UtcNow.AddDays(31);

        Assert.Empty((await service.GetAsync(owner)).Items);
        Assert.Equal(1, await service.DeleteExpiredAsync(250));
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Empty(context.Carts);
        Assert.Empty(context.CartItems);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
