using Sallvat.Domain.Orders;

namespace Sallvat.Domain.Payments;

public sealed class Payment
{
    public const int PreferenceIdMaxLength = 160;
    public const int ExternalOrderIdMaxLength = 64;

    private Payment()
    {
    }

    public Payment(
        Order order,
        PaymentEnvironment environment,
        Guid idempotencyKey,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!Enum.IsDefined(environment))
        {
            throw new ArgumentOutOfRangeException(nameof(environment));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));
        }

        ValidateTimestamp(createdAtUtc, order.CreatedAtUtc);
        if (order.Status != OrderStatus.PendingPayment || createdAtUtc >= order.ExpiresAtUtc)
        {
            throw new InvalidOperationException("Only a pending, unexpired order can start a payment.");
        }

        if (order.GrandTotal is <= 0 or > 9_999_999_999_999_999.99m)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        OrderId = order.Id;
        ExternalReference = order.OrderNumber;
        Amount = order.GrandTotal;
        Currency = order.Currency;
        Environment = environment;
        IdempotencyKey = idempotencyKey;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ExpiresAtUtc = order.ExpiresAtUtc;
    }

    public long Id { get; private set; }

    public long OrderId { get; private set; }

    public string Provider { get; private set; } = "MercadoPago";

    public PaymentEnvironment Environment { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public string ExternalReference { get; private set; } = string.Empty;

    public string? PreferenceId { get; private set; }

    public string? ExternalOrderId { get; private set; }

    public PaymentDispatchState DispatchState { get; private set; }

    public Guid? DispatchToken { get; private set; }

    public DateTimeOffset? DispatchStartedAtUtc { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;

    public PaymentAttentionReason? AttentionReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    public bool RegisterPreference(string preferenceId, DateTimeOffset receivedAtUtc)
    {
        if (DispatchState != PaymentDispatchState.NotStarted || ExternalOrderId is not null)
        {
            throw new InvalidOperationException("An Orders attempt cannot receive a preference.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(preferenceId);
        if (preferenceId.Length > PreferenceIdMaxLength
            || preferenceId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Invalid preference identifier.", nameof(preferenceId));
        }

        ValidateTimestamp(receivedAtUtc, UpdatedAtUtc);
        if (PreferenceId is not null)
        {
            if (PreferenceId == preferenceId)
            {
                return false;
            }

            throw new InvalidOperationException("A preference cannot be replaced.");
        }

        if (Status is not PaymentStatus.Created and not PaymentStatus.RequiresAttention)
        {
            throw new InvalidOperationException("Payment cannot receive a preference in this state.");
        }

        PreferenceId = preferenceId;
        if (Status == PaymentStatus.Created)
        {
            if (receivedAtUtc >= ExpiresAtUtc)
            {
                Status = PaymentStatus.RequiresAttention;
                AttentionReason = PaymentAttentionReason.LatePreferenceResponse;
            }
            else
            {
                Status = PaymentStatus.Pending;
            }
        }

        Touch(receivedAtUtc);
        return true;
    }

    public bool MarkOutcomeUnknown(DateTimeOffset occurredAtUtc)
    {
        if (DispatchState != PaymentDispatchState.NotStarted)
        {
            throw new InvalidOperationException("Use the Orders dispatch result for this attempt.");
        }

        ValidateTimestamp(occurredAtUtc, UpdatedAtUtc);
        if (Status == PaymentStatus.RequiresAttention)
        {
            return false;
        }

        if (Status is not PaymentStatus.Created and not PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only an unresolved payment can have an unknown outcome.");
        }

        Status = PaymentStatus.RequiresAttention;
        AttentionReason = PaymentAttentionReason.PreferenceOutcomeUnknown;
        Touch(occurredAtUtc);
        return true;
    }

    public bool TryBeginOrderDispatch(Guid token, DateTimeOffset timestamp)
    {
        ValidateTimestamp(timestamp, UpdatedAtUtc);
        if (token == Guid.Empty)
        {
            throw new ArgumentException("A dispatch token is required.", nameof(token));
        }

        if (DispatchState != PaymentDispatchState.NotStarted || Status != PaymentStatus.Created
            || PreferenceId is not null || Environment != PaymentEnvironment.Sandbox || timestamp >= ExpiresAtUtc)
        {
            return false;
        }

        DispatchToken = token;
        DispatchStartedAtUtc = timestamp;
        DispatchState = PaymentDispatchState.Sending;
        Touch(timestamp);
        return true;
    }

    public void CompleteOrderDispatch(Guid token, string? externalOrderId, bool orderStillPayable, DateTimeOffset timestamp)
    {
        ValidateTimestamp(timestamp, UpdatedAtUtc);
        if (token == Guid.Empty || DispatchToken != token || DispatchState != PaymentDispatchState.Sending)
        {
            throw new InvalidOperationException("Only the owner of an unresolved dispatch can complete it.");
        }

        if (externalOrderId is not null && (externalOrderId.Length > ExternalOrderIdMaxLength
            || !externalOrderId.StartsWith("ORD", StringComparison.Ordinal)
            || !externalOrderId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("Invalid external order identifier.", nameof(externalOrderId));
        }

        ExternalOrderId = externalOrderId;
        if (externalOrderId is not null && orderStillPayable && timestamp < ExpiresAtUtc)
        {
            DispatchState = PaymentDispatchState.Completed;
            Status = PaymentStatus.Pending;
        }
        else
        {
            DispatchState = PaymentDispatchState.RequiresAttention;
            Status = PaymentStatus.RequiresAttention;
            AttentionReason = externalOrderId is null ? PaymentAttentionReason.OrderOutcomeUnknown : PaymentAttentionReason.LateOrderResponse;
        }

        Touch(timestamp);
    }

    private static void ValidateTimestamp(DateTimeOffset timestamp, DateTimeOffset minimum)
    {
        if (timestamp.Offset != TimeSpan.Zero || timestamp < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A nondecreasing UTC timestamp is required.");
        }
    }

    private void Touch(DateTimeOffset timestamp)
    {
        UpdatedAtUtc = timestamp;
        ConcurrencyVersion = Guid.NewGuid();
    }
}
