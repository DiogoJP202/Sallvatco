namespace Sallvat.Infrastructure.Orders;

public sealed class OrderOptions
{
    public const string SectionName = "Orders";

    public int ReservationMinutes { get; set; } = 30;
}
