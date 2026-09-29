using Sallvat.Application.Payments;

namespace Sallvat.Web.Payments;

public sealed partial class PaymentRecoveryWorker(IServiceScopeFactory scopeFactory, ILogger<PaymentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunBatchAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task RunBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var candidates = await scope.ServiceProvider.GetRequiredService<IPaymentRecoveryBatchService>().RunAsync(20, cancellationToken);
            if (candidates > 0) { LogBatch(logger, candidates); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never log provider bodies, database command values, credentials or customer data.
            LogFailure(logger);
        }
    }

    [LoggerMessage(EventId = 4301, Level = LogLevel.Information, Message = "Automatic recovery examined {CandidateCount} payment candidates; see durable execution outcomes.")]
    private static partial void LogBatch(ILogger logger, int candidateCount);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Error, Message = "Automatic payment recovery batch could not complete. Inspect service health and execution history.")]
    private static partial void LogFailure(ILogger logger);
}
