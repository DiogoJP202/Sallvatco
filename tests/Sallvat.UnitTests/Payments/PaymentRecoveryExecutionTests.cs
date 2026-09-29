using Sallvat.Domain.Payments;

namespace Sallvat.UnitTests.Payments;

public sealed class PaymentRecoveryExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompletedExecutionHasBoundedWindowAndCannotFinishTwice()
    {
        var execution = new PaymentRecoveryExecution(Guid.NewGuid(), 1, Now);
        var version = execution.ConcurrencyVersion;
        Assert.Equal(Now.AddMinutes(2), execution.ExpiresAtUtc);
        Assert.Throws<InvalidOperationException>(() => execution.Complete(Now.AddMinutes(-1)));
        Assert.Throws<InvalidOperationException>(() => execution.Interrupt(Now));
        execution.Complete(Now.AddSeconds(1));
        Assert.Equal(PaymentRecoveryExecutionState.Completed, execution.State);
        Assert.Equal(Now.AddSeconds(1), execution.FinishedAtUtc);
        Assert.NotEqual(version, execution.ConcurrencyVersion);
        Assert.Throws<InvalidOperationException>(() => execution.Complete(Now.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => execution.Interrupt(Now.AddMinutes(2)));
    }

    [Fact]
    public void ExpiredExecutionCanOnlyBeInterruptedAndRequiresUtcIdentity()
    {
        Assert.Throws<ArgumentException>(() => new PaymentRecoveryExecution(Guid.Empty, 1, Now));
        Assert.Throws<ArgumentException>(() => new PaymentRecoveryExecution(Guid.NewGuid(), 0, Now));
        Assert.Throws<ArgumentException>(() => new PaymentRecoveryExecution(Guid.NewGuid(), 1, Now.ToOffset(TimeSpan.FromHours(1))));
        var execution = new PaymentRecoveryExecution(Guid.NewGuid(), 1, Now);
        Assert.Throws<InvalidOperationException>(() => execution.Complete(execution.ExpiresAtUtc));
        Assert.Throws<InvalidOperationException>(() => execution.Complete(Now.ToOffset(TimeSpan.FromHours(1))));
        execution.Interrupt(execution.ExpiresAtUtc);
        Assert.Equal(PaymentRecoveryExecutionState.Interrupted, execution.State);
        Assert.Equal(execution.ExpiresAtUtc, execution.FinishedAtUtc);
    }
}
