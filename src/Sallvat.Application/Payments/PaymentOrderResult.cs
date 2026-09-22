namespace Sallvat.Application.Payments;

public enum PaymentOrderStatus
{
    Created,
    Disabled,
    InvalidRequest,
    ConfigurationInvalid,
    AuthenticationFailure,
    Rejected,
    OutcomeUnknown,
}

// An external order is not a preference and Created is not payment confirmation.
public sealed record PaymentOrderResult(
    PaymentOrderStatus Status,
    string? ExternalOrderId = null,
    Uri? CheckoutUrl = null);
