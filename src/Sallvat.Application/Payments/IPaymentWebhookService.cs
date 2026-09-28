namespace Sallvat.Application.Payments;

public sealed record PaymentWebhookRequest(string DataId, string RequestId, string Signature, byte[] Body);

public enum PaymentWebhookResult
{
    Accepted,
    Disabled,
    Invalid,
    Unauthorized,
    Retry,
}

public interface IPaymentWebhookService
{
    Task<PaymentWebhookResult> HandleAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default);
}
