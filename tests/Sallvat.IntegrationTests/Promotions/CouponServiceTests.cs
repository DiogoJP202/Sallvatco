using Microsoft.Extensions.DependencyInjection;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Promotions;

public sealed class CouponServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AdministratorCreatesUpdatesAndAuditsCoupon()
    {
        await using var application = new AccountWebApplicationFactory(
            clock: new MutableClock(Now));
        await application.InitializeDatabaseAsync();
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICouponService>();
        var actor = Guid.NewGuid();

        var created = await service.CreateAsync(
            Input(" boas-vindas10 "),
            new(actor, "coupon-create"));
        var id = Assert.IsType<long>(created.EntityId);
        var details = await service.GetAdminAsync(id);

        Assert.Equal("BOAS-VINDAS10", details!.Coupon.Code);
        var updated = await service.UpdateAsync(
            id,
            details.ConcurrencyVersion,
            details.Coupon with { Value = 15m },
            new(actor, "coupon-update"));
        Assert.True(updated.Succeeded);
        Assert.Equal(15m, (await service.GetAdminAsync(id))!.Coupon.Value);

        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        Assert.Equal(2, context.AuditLogs.Count(log =>
            log.EntityType == nameof(Coupon)));
    }

    [Fact]
    public async Task ReservationIsIdempotentAndIdentityLimitIsEnforced()
    {
        await using var application = new AccountWebApplicationFactory(
            clock: new MutableClock(Now));
        await application.InitializeDatabaseAsync();
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICouponService>();
        await service.CreateAsync(
            Input("CLIENTE1") with { UsageLimitPerIdentity = 1 },
            new(Guid.NewGuid(), "coupon-limit"));
        var key = Guid.NewGuid();
        var request = Request("CLIENTE1", key, "pessoa@example.com");

        var first = await service.ReserveAsync(request);
        var replay = await service.ReserveAsync(request);
        var second = await service.ReserveAsync(Request(
            "CLIENTE1",
            Guid.NewGuid(),
            "PESSOA@example.com"));

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.Equal(
            first.Reservation!.RedemptionId,
            replay.Reservation!.RedemptionId);
        Assert.Equal(CouponMutationStatus.Unavailable, second.Status);
        Assert.Contains("por cliente", Assert.Single(second.Errors));
    }

    [Fact]
    public async Task OnlyOneConcurrentReservationClaimsTheLastGlobalUse()
    {
        await using var application = new AccountWebApplicationFactory(
            clock: new MutableClock(Now));
        await application.InitializeDatabaseAsync();
        using (var setupScope = application.Services.CreateScope())
        {
            var setup = setupScope.ServiceProvider
                .GetRequiredService<ICouponService>();
            await setup.CreateAsync(
                Input("ULTIMO1") with { TotalUsageLimit = 1 },
                new(Guid.NewGuid(), "coupon-race"));
        }

        using var firstScope = application.Services.CreateScope();
        using var secondScope = application.Services.CreateScope();
        var firstService = firstScope.ServiceProvider
            .GetRequiredService<ICouponService>();
        var secondService = secondScope.ServiceProvider
            .GetRequiredService<ICouponService>();

        var results = await Task.WhenAll(
            firstService.ReserveAsync(Request(
                "ULTIMO1",
                Guid.NewGuid(),
                "primeira@example.com")),
            secondService.ReserveAsync(Request(
                "ULTIMO1",
                Guid.NewGuid(),
                "segunda@example.com")));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result =>
            result.Status == CouponMutationStatus.Unavailable);
    }

    [Fact]
    public async Task ReservationCanBeConsumedOrReleasedIdempotently()
    {
        var clock = new MutableClock(Now);
        await using var application = new AccountWebApplicationFactory(
            clock: clock);
        await application.InitializeDatabaseAsync();
        using var scope = application.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICouponService>();
        await service.CreateAsync(
            Input("FLUXO10"),
            new(Guid.NewGuid(), "coupon-flow"));

        var consumedKey = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(Request(
            "FLUXO10",
            consumedKey,
            "consume@example.com"))).Succeeded);
        Assert.True((await service.ConsumeAsync(consumedKey, 1001)).Succeeded);
        Assert.True((await service.ConsumeAsync(consumedKey, 1001)).Succeeded);
        Assert.Equal(CouponMutationStatus.Unavailable,
            (await service.ReleaseAsync(consumedKey)).Status);

        var expiringKey = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(Request(
            "FLUXO10",
            expiringKey,
            "expire@example.com"))).Succeeded);
        clock.UtcNow = Now.AddMinutes(31);
        Assert.Equal(1, await service.ReleaseExpiredAsync(10));
        Assert.Equal(CouponMutationStatus.Unavailable,
            (await service.ConsumeAsync(expiringKey, 1002)).Status);

        var details = Assert.Single(await service.ListAdminAsync());
        Assert.Equal(1, details.ClaimedUsageCount);
    }

    private static CouponEditorInput Input(string code) => new(
        code,
        CouponDiscountType.Percentage,
        10,
        50,
        null,
        null,
        null,
        null,
        true);

    private static CouponReservationRequest Request(
        string code,
        Guid key,
        string email) => new(
            code,
            key,
            null,
            email,
            [
                new CouponDiscountLine(1, 40m),
                new CouponDiscountLine(2, 60m),
            ],
            Now.AddMinutes(30));

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
