using Sallvat.Application.Orders;

namespace Sallvat.Web.Orders;

public sealed partial class OrderExpirationService(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderExpirationService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExpireBatchAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IOrderLifecycleService>();
            var expired = await service.ExpirePendingAsync(
                BatchSize,
                cancellationToken);
            if (expired > 0)
            {
                LogExpiredOrders(logger, expired);
            }
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            LogExpirationFailure(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Information,
        Message = "Expired {OrderCount} pending orders.")]
    private static partial void LogExpiredOrders(
        ILogger logger,
        int orderCount);

    [LoggerMessage(
        EventId = 4202,
        Level = LogLevel.Error,
        Message = "Could not expire pending orders.")]
    private static partial void LogExpirationFailure(
        ILogger logger,
        Exception exception);
}
