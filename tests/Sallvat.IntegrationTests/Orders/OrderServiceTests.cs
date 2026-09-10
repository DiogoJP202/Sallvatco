using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Catalog;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Orders;

public sealed class OrderServiceTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatesImmutableSnapshotsAndReplaysAttemptIdempotently()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-snapshot");
        var owner = CartOwner.ForGuest(Token('A'));
        var attemptId = Guid.NewGuid();

        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            2)).Succeeded);
        var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var request = Request(attemptId, owner, clock.UtcNow);

        var created = await service.CreateAsync(request);
        var replay = await service.CreateAsync(request);

        Assert.True(created.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.True(replay.WasAlreadyCreated);
        Assert.Equal(created.Order!.Id, replay.Order!.Id);
        Assert.Equal(OrderStatus.PendingPayment, created.Order.Status);
        Assert.Equal(618.30m, created.Order.GrandTotal);
        Assert.Equal(
            InitialTime.AddMinutes(30),
            created.Order.ExpiresAtUtc);

        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        context.ChangeTracker.Clear();
        var order = await context.Orders.SingleAsync();
        var item = await context.OrderItems.SingleAsync();
        var address = await context.OrderAddresses.SingleAsync();
        var reservation = await context.StockReservations.SingleAsync();
        var variant = await context.ProductVariants.SingleAsync(candidate =>
            candidate.Id == product.AvailableVariantId);
        var cart = await context.Carts.SingleAsync(candidate =>
            candidate.Id == order.SourceCartId);

        Assert.StartsWith("SVT-20260910-", order.OrderNumber);
        Assert.NotNull(order.CustomerId);
        Assert.Null((await context.Customers.SingleAsync()).ApplicationUserId);
        Assert.Equal("Cliente Teste", order.BuyerName);
        Assert.Equal("cliente@example.com", order.BuyerEmail);
        Assert.Equal("Âmbar Noturno", item.ProductName);
        Assert.Equal("50 ml", item.VariantName);
        Assert.Equal(599.80m, item.Subtotal);
        Assert.Equal("Avenida Paulista", address.Street);
        Assert.Equal(2, reservation.Quantity);
        Assert.Equal(StockReservationStatus.Reserved, reservation.Status);
        Assert.Equal(2, variant.Reserved);
        Assert.Null(cart.CouponId);
        Assert.Empty(await context.CartItems.ToListAsync());

        var originalSku = item.Sku;
        variant.UpdateCommercialData(
            "CATALOG-CHANGED-50",
            50,
            399.90m,
            variant.WeightKg,
            variant.HeightCm,
            variant.WidthCm,
            variant.LengthCm,
            true,
            clock.UtcNow.AddMinutes(1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        item = await context.OrderItems.SingleAsync();
        Assert.Equal(originalSku, item.Sku);
        Assert.Equal(299.90m, item.UnitPrice);
    }

    [Fact]
    public async Task CouponIsRevalidatedAllocatedAndConsumedWithOrder()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-coupon");
        var owner = CartOwner.ForGuest(Token('B'));

        using var scope = application.Services.CreateScope();
        var couponService = scope.ServiceProvider
            .GetRequiredService<ICouponService>();
        var couponCreated = await couponService.CreateAsync(
            new CouponEditorInput(
                "PEDIDO10",
                CouponDiscountType.Percentage,
                10m,
                0m,
                null,
                null,
                10,
                1,
                true),
            new(Guid.NewGuid(), "order-coupon"));
        Assert.True(couponCreated.Succeeded);
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);
        Assert.True((await cartService.ApplyCouponAsync(
            owner,
            "PEDIDO10")).Succeeded);
        var service = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var result = await service.CreateAsync(
            Request(Guid.NewGuid(), owner, clock.UtcNow));

        Assert.True(result.Succeeded);
        Assert.Equal(288.41m, result.Order!.GrandTotal);
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        context.ChangeTracker.Clear();
        var order = await context.Orders.SingleAsync();
        var item = await context.OrderItems.SingleAsync();
        var redemption = await context.CouponRedemptions.SingleAsync();
        var coupon = await context.Coupons.SingleAsync();
        Assert.Equal("PEDIDO10", order.CouponCode);
        Assert.Equal(29.99m, order.DiscountTotal);
        Assert.Equal(29.99m, item.DiscountAmount);
        Assert.Equal(269.91m, item.Subtotal);
        Assert.Equal(CouponRedemptionStatus.Consumed, redemption.Status);
        Assert.Equal(order.Id, redemption.OrderId);
        Assert.Equal(1, coupon.ClaimedUsageCount);
    }

    [Fact]
    public async Task ConcurrentCheckoutsHaveOneWinnerForLastUnit()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-last-unit");
        await SetStockAsync(
            application,
            product.AvailableVariantId,
            1,
            clock.UtcNow.AddMinutes(1));
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        var firstOwner = CartOwner.ForGuest(Token('C'));
        var secondOwner = CartOwner.ForGuest(Token('D'));
        await AddItemAsync(
            application,
            firstOwner,
            product.AvailableVariantId);
        await AddItemAsync(
            application,
            secondOwner,
            product.AvailableVariantId);

        var results = await Task.WhenAll(
            CreateInScopeAsync(
                application,
                Request(Guid.NewGuid(), firstOwner, clock.UtcNow)),
            CreateInScopeAsync(
                application,
                Request(Guid.NewGuid(), secondOwner, clock.UtcNow)));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result =>
            result.Status == OrderCreationStatus.Unavailable);
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Equal(1, await context.Orders.CountAsync());
        Assert.Equal(1, await context.Customers.CountAsync());
        Assert.Equal(1, await context.StockReservations.CountAsync());
        Assert.Equal(
            1,
            (await context.ProductVariants.SingleAsync(variant =>
                variant.Id == product.AvailableVariantId)).Reserved);
        Assert.Equal(1, await context.CartItems.CountAsync());
    }

    [Fact]
    public async Task FailedSecondReservationLeavesNoPartialEffects()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var firstProduct = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-rollback-first");
        var secondProduct = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-rollback-second");
        var owner = CartOwner.ForGuest(Token('E'));
        await AddItemAsync(
            application,
            owner,
            firstProduct.AvailableVariantId);
        await AddItemAsync(
            application,
            owner,
            secondProduct.AvailableVariantId);
        await SetStockAsync(
            application,
            secondProduct.AvailableVariantId,
            0,
            clock.UtcNow.AddMinutes(1));
        clock.UtcNow = clock.UtcNow.AddMinutes(2);

        var result = await CreateInScopeAsync(
            application,
            Request(Guid.NewGuid(), owner, clock.UtcNow));

        Assert.Equal(OrderCreationStatus.Unavailable, result.Status);
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.StockReservations.ToListAsync());
        Assert.Equal(2, await context.CartItems.CountAsync());
        Assert.Equal(
            0,
            (await context.ProductVariants.SingleAsync(variant =>
                variant.Id == firstProduct.AvailableVariantId)).Reserved);
    }

    [Fact]
    public async Task InvalidShippingSnapshotCannotCreateOrAlterOrderState()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "order-invalid-shipping");
        var owner = CartOwner.ForGuest(Token('F'));
        await AddItemAsync(
            application,
            owner,
            product.AvailableVariantId);
        var request = Request(Guid.NewGuid(), owner, clock.UtcNow);
        request = request with
        {
            Shipping = request.Shipping with { Price = 0m },
        };

        var result = await CreateInScopeAsync(application, request);

        Assert.Equal(OrderCreationStatus.Invalid, result.Status);
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.StockReservations.ToListAsync());
        Assert.Single(await context.CartItems.ToListAsync());
        Assert.Equal(
            0,
            (await context.ProductVariants.SingleAsync(variant =>
                variant.Id == product.AvailableVariantId)).Reserved);
    }

    private static CreateOrderRequest Request(
        Guid attemptId,
        CartOwner owner,
        DateTimeOffset now) =>
        new(
            attemptId,
            owner,
            ValidCheckout(),
            new CheckoutShippingSnapshot(
                "melhor-envio",
                "Transportadora Teste",
                "Expresso",
                $"quote-{attemptId:N}",
                18.50m,
                "BRL",
                2,
                4,
                now.AddMinutes(-1),
                now.AddMinutes(10)));

    private static CheckoutDraftInput ValidCheckout() =>
        new(
            new(
                " Cliente Teste ",
                " CLIENTE@EXAMPLE.COM ",
                "(11) 99999-8888"),
            new(
                null,
                "Cliente Teste",
                "01310-100",
                "Avenida Paulista",
                "1000",
                null,
                "Bela Vista",
                "São Paulo",
                "sp"));

    private static string Token(char value) => new(value, 43);

    private static async Task AddItemAsync(
        AccountWebApplicationFactory application,
        CartOwner owner,
        long variantId)
    {
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        Assert.True((await cartService.AddItemAsync(
            owner,
            variantId,
            1)).Succeeded);
    }

    private static async Task SetStockAsync(
        AccountWebApplicationFactory application,
        long variantId,
        int stock,
        DateTimeOffset timestamp)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var variant = await context.ProductVariants.SingleAsync(item =>
            item.Id == variantId);
        variant.AdjustOnHand(stock, timestamp);
        await context.SaveChangesAsync();
    }

    private static async Task<OrderCreationResult> CreateInScopeAsync(
        AccountWebApplicationFactory application,
        CreateOrderRequest request)
    {
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
        return await service.CreateAsync(request);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
