using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Payments;

namespace Sallvat.Web.Models.Payments;

public sealed class PaymentRecoveryViewModel
{
    public Guid ExpectedVersion { get; set; }

    [Required]
    [EnumDataType(typeof(PaymentRecoveryReason))]
    public PaymentRecoveryReason? Reason { get; set; }

    public bool Confirm { get; set; }
}
