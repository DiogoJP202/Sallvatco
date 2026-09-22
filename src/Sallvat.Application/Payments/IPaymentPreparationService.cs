using Sallvat.Application.Carts;
using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

public interface IPaymentPreparationService
{
    Task<PaymentPreparationResult> PrepareAsync(
        long orderId,
        CartOwner owner,
        PaymentEnvironment environment,
        CancellationToken cancellationToken = default);
}

public enum PaymentPreparationStatus
{
    Prepared,
    AlreadyPrepared,
    NotFound,
    Invalid,
    RequiresAttention,
    Conflict,
}

public sealed record PaymentPreparationResult(
    PaymentPreparationStatus Status,
    long? PaymentId = null);
