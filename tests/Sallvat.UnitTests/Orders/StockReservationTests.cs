using Sallvat.Domain.Inventory;

namespace Sallvat.UnitTests.Orders;

public sealed class StockReservationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReleaseIsIdempotentAndChangesConcurrencyVersion()
    {
        var reservation = Create();
        var initialVersion = reservation.ConcurrencyVersion;

        Assert.True(reservation.Release(Now.AddMinutes(5)));
        Assert.Equal(StockReservationStatus.Released, reservation.Status);
        Assert.Equal(Now.AddMinutes(5), reservation.ReleasedAtUtc);
        Assert.NotEqual(initialVersion, reservation.ConcurrencyVersion);
        Assert.False(reservation.Release(Now.AddMinutes(6)));
    }

    [Fact]
    public void ConsumedReservationCannotBeReleased()
    {
        var reservation = Create();

        Assert.True(reservation.Consume(Now.AddMinutes(5)));
        Assert.False(reservation.Consume(Now.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.Release(Now.AddMinutes(7)));
    }

    private static StockReservation Create() =>
        new(1, 2, 3, Now, Now.AddMinutes(30));
}
