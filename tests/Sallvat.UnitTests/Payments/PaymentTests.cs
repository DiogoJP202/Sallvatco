using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.UnitTests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CopiesOnlyCommercialSnapshotFromPendingOrder()
    {
        var order = CreateOrder();
        var key = Guid.NewGuid();
        var payment = new Payment(order, PaymentEnvironment.Sandbox, key, Now);

        Assert.Equal(order.Id, payment.OrderId);
        Assert.Equal(order.OrderNumber, payment.ExternalReference);
        Assert.Equal(158.50m, payment.Amount);
        Assert.Equal("BRL", payment.Currency);
        Assert.Equal("MercadoPago", payment.Provider);
        Assert.Equal(PaymentEnvironment.Sandbox, payment.Environment);
        Assert.Equal(key, payment.IdempotencyKey);
        Assert.Equal(order.ExpiresAtUtc, payment.ExpiresAtUtc);
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.PreferenceId);
        Assert.Null(payment.AttentionReason);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.RequiresAttention)]
    public void RejectsOrdersThatAreNotPending(OrderStatus status)
    {
        var order = CreateOrder();
        order.TransitionTo(status, Now, "Revisão de teste.");
        Assert.Throws<InvalidOperationException>(() =>
            new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void RejectsExpiredOrders(int minutes)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Payment(CreateOrder(), PaymentEnvironment.Sandbox, Guid.NewGuid(), Now.AddMinutes(minutes)));
    }

    [Fact]
    public void RejectsInvalidEnvironmentKeyAndTimestamps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Payment(CreateOrder(), (PaymentEnvironment)42, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() =>
            new Payment(CreateOrder(), PaymentEnvironment.Sandbox, Guid.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Payment(CreateOrder(), PaymentEnvironment.Sandbox, Guid.NewGuid(), Now.AddSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Payment(CreateOrder(), PaymentEnvironment.Sandbox, Guid.NewGuid(), Now.ToOffset(TimeSpan.FromHours(-3))));
    }

    [Fact]
    public void RegisteringPreferenceIsIdempotentAndDoesNotPayOrder()
    {
        var order = CreateOrder();
        var payment = new Payment(order, PaymentEnvironment.Sandbox, Guid.NewGuid(), Now);
        var originalVersion = payment.ConcurrencyVersion;
        Assert.True(payment.RegisterPreference("preference-123", Now.AddSeconds(1)));
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.NotEqual(originalVersion, payment.ConcurrencyVersion);
        var version = payment.ConcurrencyVersion;
        Assert.False(payment.RegisterPreference("preference-123", Now.AddSeconds(2)));
        Assert.Equal(version, payment.ConcurrencyVersion);
        Assert.Equal(Now.AddSeconds(1), payment.UpdatedAtUtc);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Throws<InvalidOperationException>(() => payment.RegisterPreference("different-123", Now.AddSeconds(3)));
        Assert.Equal("preference-123", payment.PreferenceId);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void LatePreferenceIsRecordedForReviewWithoutReopeningPayment(int minutes)
    {
        var payment = Create();
        payment.RegisterPreference("preference-123", Now.AddMinutes(minutes));
        Assert.Equal("preference-123", payment.PreferenceId);
        Assert.Equal(PaymentStatus.RequiresAttention, payment.Status);
        Assert.Equal(PaymentAttentionReason.LatePreferenceResponse, payment.AttentionReason);
    }

    [Fact]
    public void UnknownOutcomeCannotBeSilentlyClearedByReceivingPreference()
    {
        var payment = Create();
        Assert.True(payment.MarkOutcomeUnknown(Now.AddSeconds(1)));
        var version = payment.ConcurrencyVersion;
        Assert.False(payment.MarkOutcomeUnknown(Now.AddSeconds(2)));
        Assert.Equal(version, payment.ConcurrencyVersion);
        payment.RegisterPreference("preference-123", Now.AddSeconds(3));
        Assert.Equal(PaymentStatus.RequiresAttention, payment.Status);
        Assert.Equal(PaymentAttentionReason.PreferenceOutcomeUnknown, payment.AttentionReason);
    }

    [Fact]
    public void PendingPaymentCanBeFlaggedWithoutLosingPreference()
    {
        var payment = Create();
        payment.RegisterPreference("preference-123", Now);
        payment.MarkOutcomeUnknown(Now.AddSeconds(1));
        Assert.Equal(PaymentStatus.RequiresAttention, payment.Status);
        Assert.Equal("preference-123", payment.PreferenceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid\nidentifier")]
    [InlineData("<script>")]
    public void RejectsInvalidPreferenceWithoutMutation(string identifier)
    {
        var payment = Create();
        Assert.Throws<ArgumentException>(() => payment.RegisterPreference(identifier, Now));
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.PreferenceId);
    }

    [Fact]
    public void RejectsOversizedIdentifierAndNonMonotonicOrNonUtcUpdates()
    {
        var payment = Create();
        Assert.Throws<ArgumentException>(() => payment.RegisterPreference(new string('a', 161), Now));
        payment.MarkOutcomeUnknown(Now.AddMinutes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.RegisterPreference("preference-123", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.MarkOutcomeUnknown(Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.MarkOutcomeUnknown(Now.AddMinutes(2).ToOffset(TimeSpan.FromHours(1))));
        Assert.Null(payment.PreferenceId);
    }

    private static Payment Create() => new(CreateOrder(), PaymentEnvironment.Sandbox, Guid.NewGuid(), Now);

    [Fact]
    public void PreparingAttemptVersionsTheOrderWithoutChangingStatus()
    {
        var order = CreateOrder();
        var version = order.ConcurrencyVersion;
        order.RegisterPaymentAttempt(Now.AddSeconds(1));
        Assert.NotEqual(version, order.ConcurrencyVersion);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => order.RegisterPaymentAttempt(Now));
        Assert.Throws<ArgumentException>(() => order.RegisterPaymentAttempt(Now.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<InvalidOperationException>(() => order.RegisterPaymentAttempt(order.ExpiresAtUtc));
        order.TransitionTo(OrderStatus.Cancelled, Now.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() => order.RegisterPaymentAttempt(Now.AddSeconds(3)));
    }

    private static Order CreateOrder() => new(
        1_000, "SVT-20260921-00001000", Guid.NewGuid(), Guid.NewGuid(), null,
        "Cliente Teste", "cliente@example.com", "11999998888",
        140m, 0m, 18.50m, "BRL", null, null,
        "Melhor Envio", "Transportadora Teste", "Expresso", "quote-123", 2, 4,
        Now.AddMinutes(-1), Now, Now.AddMinutes(30));
}
