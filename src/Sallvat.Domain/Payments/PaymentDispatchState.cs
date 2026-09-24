namespace Sallvat.Domain.Payments;

public enum PaymentDispatchState
{
    NotStarted,
    Sending,
    Completed,
    RequiresAttention,
}
