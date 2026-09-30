namespace Sallvat.Domain.Payments;

public enum WebhookOutcome
{
    Observed,
    Confirmed,
    RequiresAttention,
    Refunded,
}

// Minimal immutable receipt/audit. Never stores headers, signatures, buyer information or raw JSON.
public sealed class WebhookEvent
{
    private WebhookEvent() { }

    public WebhookEvent(string deliveryKey, long paymentId, string externalOrderId, WebhookOutcome outcome, DateTimeOffset now)
    {
        if (deliveryKey.Length != 64 || !deliveryKey.All(char.IsAsciiHexDigit) || paymentId <= 0
            || string.IsNullOrEmpty(externalOrderId) || externalOrderId.Length > 64
            || !Enum.IsDefined(outcome) || now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid webhook receipt.");
        }

        DeliveryKey = deliveryKey;
        PaymentId = paymentId;
        ExternalOrderId = externalOrderId;
        Outcome = outcome;
        ReceivedAtUtc = now;
    }

    public long Id { get; private set; }
    public string DeliveryKey { get; private set; } = string.Empty;
    public long PaymentId { get; private set; }
    public string ExternalOrderId { get; private set; } = string.Empty;
    public WebhookOutcome Outcome { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
}
