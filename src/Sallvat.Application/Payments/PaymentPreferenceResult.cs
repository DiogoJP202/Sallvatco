namespace Sallvat.Application.Payments;

public enum PaymentPreferenceStatus
{
    Created,
    Disabled,
    InvalidRequest,
    ConfigurationInvalid,
    AuthenticationFailure,
    Rejected,
    OutcomeUnknown,
}

public sealed record PaymentPreferenceResult(
    PaymentPreferenceStatus Status,
    string? PreferenceId = null,
    Uri? CheckoutUrl = null);
