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

public sealed class OrderLifecycleServiceTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExpirationReleasesStockAndCouponExactlyOnce()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "lifecycle-expiration");
        var created = await CreateOrderAsync(
            application,
            clock,
            product,
            Token('L'),
            withCoupon: true);

        clock.UtcNow = InitialTime.AddMinutes(31);
        Assert.Equal(1, await ExpireAsync(application, 100));
        Assert.Equal(0, await ExpireAsync(application, 100));

        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var order = await context.Orders.SingleAsync(item =>
            item.Id == created.OrderId);
        var reservation = await context.StockReservations.SingleAsync(item =>
            item.OrderId == created.OrderId);
        var variant = await context.ProductVariants.SingleAsync(item =>
            item.Id == product.AvailableVariantId);
        var redemption = await context.CouponRedemptions.SingleAsync(item =>
            item.OrderId == created.OrderId);
        var coupon = await context.Coupons.SingleAsync(item =>
            item.Id == redemption.CouponId);
        var movements = await context.InventoryMovements
            .Where(item => item.ProductVariantId == product.AvailableVariantId)
            .ToListAsync();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(StockReservationStatus.Released, reservation.Status);
        Assert.Equal(0, variant.Reserved);
        Assert.Equal(CouponRedemptionStatus.Released, redemption.Status);
        Assert.Equal(created.OrderId, redemption.OrderId);
        Assert.Equal(0, coupon.ClaimedUsageCount);
        Assert.Single(movements, item =>
            item.Type == InventoryMovementType.Reservation);
        var release = Assert.Single(movements, item =>
            item.Type == InventoryMovementType.ReservationRelease);
        Assert.Null(release.ActorUserId);
        Assert.Empty(await context.AuditLogs
            .Where(item => item.Action == "order.status.changed")
            .ToListAsync());
    }

    [Fact]
    public async Task AdminTransitionsRequireReasonAndVersionAndAreAudited()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "lifecycle-admin");
        var created = await CreateOrderAsync(
            application,
            clock,
            product,
            Token('M'));

        var invalid = await TransitionAsync(
            application,
            created.OrderId,
            created.Version,
            OrderStatus.RequiresAttention,
            "não",
            product.ActorId);
        Assert.Equal(OrderLifecycleMutationStatus.Invalid, invalid.Status);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var review = await TransitionAsync(
            application,
            created.OrderId,
            created.Version,
            OrderStatus.RequiresAttention,
            "Pagamento informado pelo atendimento.",
            product.ActorId);
        Assert.True(review.Succeeded);

        var replay = await TransitionAsync(
            application,
            created.OrderId,
            created.Version,
            OrderStatus.RequiresAttention,
            "Esta repetição é idempotente.",
            product.ActorId);
        Assert.True(replay.Succeeded);
        Assert.True(replay.WasAlreadyApplied);

        var conflict = await TransitionAsync(
            application,
            created.OrderId,
            created.Version,
            OrderStatus.Cancelled,
            "Tentativa feita sobre uma versão antiga.",
            product.ActorId);
        Assert.Equal(
            OrderLifecycleMutationStatus.ConcurrencyConflict,
            conflict.Status);

        var currentVersion = await GetOrderVersionAsync(
            application,
            created.OrderId);
        var queue = await ListOpenAsync(application);
        var queued = Assert.Single(queue, item => item.Id == created.OrderId);
        Assert.Equal(OrderStatus.RequiresAttention, queued.Status);
        Assert.Equal(
            "Pagamento informado pelo atendimento.",
            queued.AttentionReason);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var cancellation = await TransitionAsync(
            application,
            created.OrderId,
            currentVersion,
            OrderStatus.Cancelled,
            "Cancelamento confirmado pelo atendimento.",
            product.ActorId);
        Assert.True(cancellation.Succeeded);

        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var order = await context.Orders.SingleAsync(item =>
            item.Id == created.OrderId);
        var audits = await context.AuditLogs
            .Where(item => item.Action == "order.status.changed")
            .OrderBy(item => item.Id)
            .ToListAsync();
        var release = await context.InventoryMovements.SingleAsync(item =>
            item.ProductVariantId == product.AvailableVariantId
            && item.Type == InventoryMovementType.ReservationRelease);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Null(order.AttentionReason);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, item => Assert.Equal(product.ActorId, item.ActorUserId));
        Assert.Contains("Pagamento informado", audits[0].ChangesJson);
        Assert.Contains("Cancelamento confirmado", audits[1].ChangesJson);
        Assert.Equal(product.ActorId, release.ActorUserId);
    }

    [Fact]
    public async Task ExpirationHonorsBatchLimit()
    {
        var clock = new MutableClock(InitialTime);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        var product = await PublishedCatalogFixture.CreateAsync(
            application,
            "lifecycle-batch");
        await CreateOrderAsync(
            application,
            clock,
            product,
            Token('N'));
        await CreateOrderAsync(
            application,
            clock,
            product,
            Token('O'));

        clock.UtcNow = InitialTime.AddMinutes(31);
        Assert.Equal(1, await ExpireAsync(application, 1));

        using (var scope = application.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<SallvatDbContext>();
            Assert.Equal(1, await context.Orders.CountAsync(item =>
                item.Status == OrderStatus.Cancelled));
            Assert.Equal(1, await context.Orders.CountAsync(item =>
                item.Status == OrderStatus.PendingPayment));
        }

        Assert.Equal(1, await ExpireAsync(application, 1));
    }

    private static async Task<SeededOrder> CreateOrderAsync(
        AccountWebApplicationFactory application,
        MutableClock clock,
        PublishedProductData product,
        string guestToken,
        bool withCoupon = false)
    {
        var owner = CartOwner.ForGuest(guestToken);
        using var scope = application.Services.CreateScope();
        var cartService = scope.ServiceProvider
            .GetRequiredService<ICartService>();
        Assert.True((await cartService.AddItemAsync(
            owner,
            product.AvailableVariantId,
            1)).Succeeded);
        if (withCoupon)
        {
            var couponService = scope.ServiceProvider
                .GetRequiredService<ICouponService>();
            var coupon = await couponService.CreateAsync(
                new CouponEditorInput(
                    $"CICLO{guestToken[0]}",
                    CouponDiscountType.Percentage,
                    10m,
                    0m,
                    null,
                    null,
                    10,
                    1,
                    true),
                new(product.ActorId, "lifecycle-test"));
            Assert.True(coupon.Succeeded);
            Assert.True((await cartService.ApplyCouponAsync(
                owner,
                $"CICLO{guestToken[0]}")).Succeeded);
        }

        var attemptId = Guid.NewGuid();
        var request = new CreateOrderRequest(
            attemptId,
            owner,
            new CheckoutDraftInput(
                new(
                    "Cliente Teste",
                    "cliente@example.com",
                    "11999998888"),
                new(
                    null,
                    "Cliente Teste",
                    "01310-100",
                    "Avenida Paulista",
                    "1000",
                    null,
                    "Bela Vista",
                    "São Paulo",
                    "SP")),
            new CheckoutShippingSnapshot(
                "melhor-envio",
                "Transportadora Teste",
                "Expresso",
                $"quote-{attemptId:N}",
                18.50m,
                "BRL",
                2,
                4,
                clock.UtcNow.AddMinutes(-1),
                clock.UtcNow.AddMinutes(10)));
        var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var result = await service.CreateAsync(request);
        Assert.True(result.Succeeded);
        var version = await scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>()
            .Orders
            .Where(item => item.Id == result.Order!.Id)
            .Select(item => item.ConcurrencyVersion)
            .SingleAsync();
        return new SeededOrder(result.Order!.Id, version);
    }

    private static async Task<OrderLifecycleMutationResult> TransitionAsync(
        AccountWebApplicationFactory application,
        long orderId,
        Guid version,
        OrderStatus target,
        string reason,
        Guid actorId)
    {
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IOrderLifecycleService>();
        return await service.TransitionAsync(
            orderId,
            version,
            target,
            reason,
            new(actorId, "order-lifecycle-test"));
    }

    private static async Task<int> ExpireAsync(
        AccountWebApplicationFactory application,
        int maximumItems)
    {
        using var scope = application.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrderLifecycleService>()
            .ExpirePendingAsync(maximumItems);
    }

    private static async Task<Guid> GetOrderVersionAsync(
        AccountWebApplicationFactory application,
        long orderId)
    {
        using var scope = application.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>()
            .Orders
            .Where(item => item.Id == orderId)
            .Select(item => item.ConcurrencyVersion)
            .SingleAsync();
    }

    private static async Task<IReadOnlyList<AdminOrderSummary>> ListOpenAsync(
        AccountWebApplicationFactory application)
    {
        using var scope = application.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrderLifecycleService>()
            .ListOpenAdminAsync();
    }

    private static string Token(char value) => new(value, 43);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed record SeededOrder(long OrderId, Guid Version);
}
