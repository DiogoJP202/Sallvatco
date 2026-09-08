using Sallvat.Application.Carts;
using Sallvat.Application.Promotions;

namespace Sallvat.Web.Carts;

public sealed partial class CartCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<CartCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 250;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await DeleteBatchAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task DeleteBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<ICartService>();
            var deleted = await service.DeleteExpiredAsync(
                BatchSize,
                cancellationToken);
            var couponService = scope.ServiceProvider
                .GetRequiredService<ICouponService>();
            var released = await couponService.ReleaseExpiredAsync(
                BatchSize,
                cancellationToken);
            if (deleted > 0)
            {
                LogDeletedCarts(logger, deleted);
            }

            if (released > 0)
            {
                LogReleasedCoupons(logger, released);
            }
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            LogCleanupFailure(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Information,
        Message = "Deleted {CartCount} expired carts.")]
    private static partial void LogDeletedCarts(
        ILogger logger,
        int cartCount);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Error,
        Message = "Could not delete expired carts.")]
    private static partial void LogCleanupFailure(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Information,
        Message = "Released {CouponCount} expired coupon reservations.")]
    private static partial void LogReleasedCoupons(
        ILogger logger,
        int couponCount);
}
