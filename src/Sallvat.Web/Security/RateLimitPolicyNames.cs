namespace Sallvat.Web.Security;

public static class RateLimitPolicyNames
{
    public const string Login = "account-login";
    public const string Registration = "account-registration";
    public const string Recovery = "account-recovery";
    public const string PaymentRecovery = "admin-payment-recovery";
    public const string CheckoutPayment = "checkout-payment";
}
