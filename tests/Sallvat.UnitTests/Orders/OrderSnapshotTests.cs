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
