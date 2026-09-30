using System.ComponentModel.DataAnnotations;
using Sallvat.Domain.Payments;

namespace Sallvat.Web.Models.Payments;

public sealed class PaymentRefundViewModel
{
    public Guid ExpectedVersion { get; set; }
    public Guid ExpectedOrderVersion { get; set; }
    [Required]
    [EnumDataType(typeof(PaymentRefundReason))]
    public PaymentRefundReason? Reason { get; set; }
    public bool Confirm { get; set; }
}
