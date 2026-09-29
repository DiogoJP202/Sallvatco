namespace Sallvat.Domain.Payments;

public enum PaymentRecoveryExecutionState { Running, Completed, Interrupted }

// Owns one bounded GET/reconciliation execution, never permission to create a charge.
public sealed class PaymentRecoveryExecution
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private PaymentRecoveryExecution() { }

    public PaymentRecoveryExecution(Guid id, long paymentId, DateTimeOffset now)
    {
        if (id == Guid.Empty || paymentId <= 0 || now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid recovery execution.");
        }

        Id = id;
        PaymentId = paymentId;
        StartedAtUtc = now;
        ExpiresAtUtc = now.Add(Lifetime);
    }

    public Guid Id { get; private set; }
    public long PaymentId { get; private set; }
    public PaymentRecoveryExecutionState State { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    public void Complete(DateTimeOffset now) => Finish(PaymentRecoveryExecutionState.Completed, now);
    public void Interrupt(DateTimeOffset now) => Finish(PaymentRecoveryExecutionState.Interrupted, now);

    private void Finish(PaymentRecoveryExecutionState state, DateTimeOffset now)
    {
        if (State != PaymentRecoveryExecutionState.Running || now.Offset != TimeSpan.Zero || now < StartedAtUtc
            || (state == PaymentRecoveryExecutionState.Completed && now >= ExpiresAtUtc)
            || (state == PaymentRecoveryExecutionState.Interrupted && now < ExpiresAtUtc))
        {
            throw new InvalidOperationException("Recovery execution cannot finish in this state or time window.");
        }

        State = state;
        FinishedAtUtc = now;
        ConcurrencyVersion = Guid.NewGuid();
    }
}
