using Sallvat.Domain.Customers;
using Sallvat.Domain.Orders;

namespace Sallvat.UnitTests.Orders;

public sealed class OrderSnapshotTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartsPendingWithConsistentCommercialSnapshot()
    {
        var order = Create(discountTotal: 14m, couponId: 12);

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(144.50m, order.GrandTotal);
        Assert.Equal("cliente@example.com", order.BuyerEmail);
        Assert.Equal("BRL", order.Currency);
    }

    [Fact]
    public void RejectsCouponSnapshotThatDoesNotMatchDiscount()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(discountTotal: 10m, couponId: null));
    }

    [Fact]
    public void RejectsNonPositiveShippingAndCouponIdentifiers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(discountTotal: 10m, couponId: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(discountTotal: 0m, couponId: null, shippingTotal: 0m));
    }

    [Fact]
    public void GuestCustomerCanBeAssignedOnlyOnceAndWithoutCredentials()
    {
        var order = Create(discountTotal: 0m, couponId: null);
        var guest = new Customer(
            "Cliente Teste",
            "cliente@example.com",
            "11999998888",
            Now);

        order.AssignGuestCustomer(guest);

        Assert.Same(guest, order.Customer);
        Assert.Throws<InvalidOperationException>(() =>
            order.AssignGuestCustomer(guest));
    }

    [Fact]
    public void AttentionTransitionStoresContextAndLeavingClearsIt()
    {
        var order = Create(discountTotal: 0m, couponId: null);
        var initialVersion = order.ConcurrencyVersion;

        Assert.True(order.TransitionTo(
            OrderStatus.RequiresAttention,
            Now.AddMinutes(1),
            "Pagamento recebido após a expiração."));
        Assert.Equal("Pagamento recebido após a expiração.", order.AttentionReason);
        Assert.Equal(Now.AddMinutes(1), order.AttentionSinceUtc);
        Assert.NotEqual(initialVersion, order.ConcurrencyVersion);
        Assert.False(order.TransitionTo(
            OrderStatus.RequiresAttention,
            Now.AddMinutes(2),
            "Nova tentativa não deve sobrescrever o motivo."));

        Assert.True(order.TransitionTo(
            OrderStatus.Cancelled,
            Now.AddMinutes(3)));
        Assert.Null(order.AttentionReason);
        Assert.Null(order.AttentionSinceUtc);
    }

    [Fact]
    public void StateMachineExposesOnlyTheDocumentedEdges()
    {
        var expected = new HashSet<(OrderStatus Source, OrderStatus Target)>
        {
            (OrderStatus.PendingPayment, OrderStatus.Paid),
            (OrderStatus.PendingPayment, OrderStatus.Cancelled),
            (OrderStatus.PendingPayment, OrderStatus.RequiresAttention),
            (OrderStatus.Paid, OrderStatus.Preparing),
            (OrderStatus.Paid, OrderStatus.Refunded),
            (OrderStatus.Paid, OrderStatus.RequiresAttention),
            (OrderStatus.Preparing, OrderStatus.Shipped),
            (OrderStatus.Preparing, OrderStatus.Refunded),
            (OrderStatus.Preparing, OrderStatus.RequiresAttention),
            (OrderStatus.Shipped, OrderStatus.Delivered),
            (OrderStatus.Shipped, OrderStatus.Refunded),
            (OrderStatus.Shipped, OrderStatus.RequiresAttention),
            (OrderStatus.Delivered, OrderStatus.Refunded),
            (OrderStatus.RequiresAttention, OrderStatus.Paid),
            (OrderStatus.RequiresAttention, OrderStatus.Preparing),
            (OrderStatus.RequiresAttention, OrderStatus.Cancelled),
            (OrderStatus.RequiresAttention, OrderStatus.Refunded),
        };

        foreach (var source in Enum.GetValues<OrderStatus>())
        {
            foreach (var target in Enum.GetValues<OrderStatus>())
            {
                Assert.Equal(
                    expected.Contains((source, target)),
                    Order.CanTransition(source, target));
            }
        }
    }

    [Fact]
    public void InvalidOrTerminalTransitionIsRejected()
    {
        var order = Create(discountTotal: 0m, couponId: null);
        order.TransitionTo(OrderStatus.Cancelled, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => order.TransitionTo(
            OrderStatus.Paid,
            Now.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => order.TransitionTo(
            (OrderStatus)999,
            Now.AddMinutes(2)));
    }

    private static Order Create(
        decimal discountTotal,
        long? couponId,
        decimal shippingTotal = 18.50m) =>
        new(
            1_000,
            "SVT-20260910-00001000",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Cliente Teste",
            "CLIENTE@EXAMPLE.COM",
            "11999998888",
            140m,
            discountTotal,
            shippingTotal,
            "brl",
            couponId,
            couponId.HasValue ? "EXEMPLO10" : null,
            "Melhor Envio",
            "Transportadora Teste",
            "Expresso",
            "quote-123",
            2,
            4,
            Now.AddMinutes(-1),
            Now,
            Now.AddMinutes(30));
}
