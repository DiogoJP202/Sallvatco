using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Payments;

public sealed class PaymentPreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly string GuestToken = new('A', 43);

    [Fact]
    public async Task PersistsSnapshotAndServerKeyOnceWithoutStockOrOrderStatusChanges()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var order = await db.Orders.SingleAsync();
        var oldVersion = order.ConcurrencyVersion;
        var service = scope.ServiceProvider.GetRequiredService<IPaymentPreparationService>();
        var first = await service.PrepareAsync(order.Id, Guest(), PaymentEnvironment.Sandbox);
        var replay = await service.PrepareAsync(order.Id, Guest(), PaymentEnvironment.Sandbox);

        Assert.Equal(PaymentPreparationStatus.Prepared, first.Status);
        Assert.Equal(PaymentPreparationStatus.AlreadyPrepared, replay.Status);
        Assert.Equal(first.PaymentId, replay.PaymentId);
        var payment = await db.Payments.SingleAsync();
        Assert.Equal(158.50m, payment.Amount);
        Assert.Equal("BRL", payment.Currency);
        Assert.Equal(order.OrderNumber, payment.ExternalReference);
        Assert.NotEqual(Guid.Empty, payment.IdempotencyKey);
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.PreferenceId);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.NotEqual(oldVersion, order.ConcurrencyVersion);
        var variant = await db.ProductVariants.SingleAsync();
        Assert.Equal(4, variant.OnHand);
        Assert.Equal(2, variant.Reserved);
        Assert.Equal(StockReservationStatus.Reserved, (await db.StockReservations.SingleAsync()).Status);
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentInMemoryPreparationsShareOneAttempt()
    {
        await using var app = await CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PrepareAsync(app, Guest())));
        Assert.Single(results, result => result.Status == PaymentPreparationStatus.Prepared);
        Assert.Equal(7, results.Count(result => result.Status == PaymentPreparationStatus.AlreadyPrepared));
        Assert.Single(results.Select(result => result.PaymentId).Distinct());
        using var scope = app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.ToListAsync());
    }

    [Fact]
    public async Task WrongGuestMissingOrderAndUnrelatedAccountRevealNoPayment()
    {
        await using var app = await CreateAsync();
        await PrepareAsync(app, Guest());
        foreach (var owner in new[] { CartOwner.ForGuest(new string('B', 43)), CartOwner.ForGuest("invalid"), CartOwner.ForCustomer(Guid.NewGuid()), new CartOwner(GuestToken, Guid.NewGuid()) })
        {
            var result = await PrepareAsync(app, owner);
            Assert.Equal(PaymentPreparationStatus.NotFound, result.Status);
            Assert.Null(result.PaymentId);
        }

        using var scope = app.Services.CreateScope();
        var missing = await scope.ServiceProvider.GetRequiredService<IPaymentPreparationService>()
            .PrepareAsync(99, Guest(), PaymentEnvironment.Sandbox);
        Assert.Equal(PaymentPreparationStatus.NotFound, missing.Status);
    }

    [Fact]
    public async Task LinkedAccountIsAuthorizedButOriginalGuestTokenIsNot()
    {
        var userId = Guid.NewGuid();
        await using var app = await CreateAsync(userId: userId);
        Assert.Equal(PaymentPreparationStatus.NotFound, (await PrepareAsync(app, Guest())).Status);
        Assert.Equal(PaymentPreparationStatus.Prepared, (await PrepareAsync(app, CartOwner.ForCustomer(userId))).Status);
    }

    [Theory]
    [InlineData("released")]
    [InlineData("missing")]
    [InlineData("quantity")]
    [InlineData("expiration")]
    [InlineData("items")]
    public async Task InconsistentReservationOrSnapshotDoesNotCreateAttempt(string scenario)
    {
        await using var app = await CreateAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            var reservation = await db.StockReservations.SingleAsync();
            if (scenario == "released")
            {
                reservation.Release(Now);
            }
            else if (scenario == "items")
            {
                db.OrderItems.RemoveRange(db.OrderItems);
            }
            else
            {
                db.StockReservations.Remove(reservation);
                if (scenario != "missing")
                {
                    db.StockReservations.Add(new StockReservation(1_000, reservation.ProductVariantId,
                        scenario == "quantity" ? 1 : 2, Now, Now.AddMinutes(scenario == "expiration" ? 10 : 30)));
                }
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(PaymentPreparationStatus.Invalid, (await PrepareAsync(app, Guest())).Status);
        using var check = app.Services.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.ToListAsync());
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.RequiresAttention)]
    public async Task NonPendingOrdersAreRefused(OrderStatus status)
    {
        await using var app = await CreateAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            (await db.Orders.SingleAsync()).TransitionTo(status, Now, "Revisão de teste.");
            await db.SaveChangesAsync();
        }

        Assert.Equal(PaymentPreparationStatus.Invalid, (await PrepareAsync(app, Guest())).Status);
    }

    [Fact]
    public async Task ExpiredOrderOrGuestSessionCannotPreparePayment()
    {
        var clock = new MutableClock { UtcNow = Now };
        await using var app = await CreateAsync(clock);
        clock.UtcNow = Now.AddMinutes(30);
        Assert.Equal(PaymentPreparationStatus.Invalid, (await PrepareAsync(app, Guest())).Status);
        clock.UtcNow = Now.AddHours(2);
        Assert.Equal(PaymentPreparationStatus.NotFound, (await PrepareAsync(app, Guest())).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOutcomeOrDifferentEnvironmentBlocksNewAttempt(bool production)
    {
        await using var app = await CreateAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
            var payment = new Payment(await db.Orders.SingleAsync(), production ? PaymentEnvironment.Production : PaymentEnvironment.Sandbox, Guid.NewGuid(), Now);
            if (!production)
            {
                payment.MarkOutcomeUnknown(Now);
            }

            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        Assert.Equal(PaymentPreparationStatus.RequiresAttention, (await PrepareAsync(app, Guest())).Status);
        using var check = app.Services.CreateScope();
        Assert.Single(await check.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.ToListAsync());
    }

    [Fact]
    public async Task ReloadsTrackedOrderAndDoesNotOverwritePendingChanges()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        var order = await db.Orders.SingleAsync();
        using (var other = app.Services.CreateScope())
        {
            var otherDb = other.ServiceProvider.GetRequiredService<SallvatDbContext>();
            (await otherDb.Orders.SingleAsync()).TransitionTo(OrderStatus.Cancelled, Now);
            await otherDb.SaveChangesAsync();
        }

        var service = scope.ServiceProvider.GetRequiredService<IPaymentPreparationService>();
        Assert.Equal(PaymentPreparationStatus.Invalid, (await service.PrepareAsync(1_000, Guest(), PaymentEnvironment.Sandbox)).Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        (await db.Carts.SingleAsync()).Refresh(Now.AddMinutes(1), Now.AddHours(3));
        Assert.Equal(PaymentPreparationStatus.Conflict, (await service.PrepareAsync(1_000, Guest(), PaymentEnvironment.Sandbox)).Status);
        Assert.True(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ProductionAndPreCancelledRequestDoNotWrite()
    {
        await using var app = await CreateAsync();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentPreparationService>();
        Assert.Equal(PaymentPreparationStatus.Invalid, (await service.PrepareAsync(1_000, Guest(), PaymentEnvironment.Production)).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(1_000, Guest(), PaymentEnvironment.Sandbox, cancellation.Token));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SallvatDbContext>().Payments.ToListAsync());
    }

    private static CartOwner Guest() => CartOwner.ForGuest(GuestToken);

    private static async Task<PaymentPreparationResult> PrepareAsync(AccountWebApplicationFactory app, CartOwner owner)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentPreparationService>().PrepareAsync(1_000, owner, PaymentEnvironment.Sandbox);
    }

    private static async Task<AccountWebApplicationFactory> CreateAsync(MutableClock? clock = null, Guid? userId = null)
    {
        var app = new AccountWebApplicationFactory(clock: clock ?? new MutableClock { UtcNow = Now });
        await app.InitializeDatabaseAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SallvatDbContext>();
        await SeedAsync(db, userId);
        return app;
    }

    internal static async Task SeedAsync(SallvatDbContext db, Guid? userId = null)
    {
        var customer = new Customer("Cliente", "cliente@example.com", "11999998888", Now);
        if (userId is Guid id)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = "cliente@example.com",
                NormalizedUserName = "CLIENTE@EXAMPLE.COM",
                Email = "cliente@example.com",
                NormalizedEmail = "CLIENTE@EXAMPLE.COM",
                EmailConfirmed = true,
            });
            customer.AssociateApplicationUser(id, Now);
        }

        db.Customers.Add(customer);
        var cart = Cart.CreateGuest(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GuestToken))), Now, Now.AddHours(2));
        db.Carts.Add(cart);
        var product = new Product("Perfume", "perfume", null, null, null, null, null, null, null, null, null, null, null, null, Now);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var variant = new ProductVariant(product.Id, "SKU-50", 50, 70m, .13m, 5m, 16m, 11m, Now);
        variant.AdjustOnHand(4, Now);
        Assert.True(variant.Reserve(2, Now));
        db.ProductVariants.Add(variant);
        var order = new Order(1_000, "SVT-20260921-00001000", Guid.NewGuid(), cart.Id, customer.Id,
            "Cliente", "cliente@example.com", "11999998888", 140m, 0m, 18.50m, "BRL", null, null,
            "Melhor Envio", "Transportadora", "Expresso", "quote-123", 2, 4, Now, Now, Now.AddMinutes(30));
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.OrderItems.Add(new OrderItem(order.Id, variant.Id, "Perfume", "50 ml", "SKU-50", 2, 70m, 0m));
        db.StockReservations.Add(new StockReservation(order.Id, variant.Id, 2, Now, order.ExpiresAtUtc));
        await db.SaveChangesAsync();
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
