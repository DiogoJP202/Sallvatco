namespace Sallvat.Infrastructure.Shipping;

public sealed class FulfillmentOptions
{
    public const string SectionName = "Shipping:Fulfillment";

    public int PreparationBusinessDays { get; set; } = 2;
}
