namespace Sallvat.Web.Models.Payments;

public sealed class PaymentRefundActionViewModel
{
    public Guid ExpectedRefundVersion { get; set; }
    public bool Confirm { get; set; }
}
