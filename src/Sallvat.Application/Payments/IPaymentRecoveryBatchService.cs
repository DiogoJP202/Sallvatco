namespace Sallvat.Application.Payments;

// Internal worker entry point; never exposed as an HTTP action.
public interface IPaymentRecoveryBatchService
{
    Task<int> RunAsync(int batchSize, CancellationToken cancellationToken = default);
}
