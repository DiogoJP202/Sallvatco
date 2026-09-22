using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

// Server-owned net line amounts; shipping is included as an explicit line, never counted twice.
public sealed record PaymentOrderRequest(
    Guid IdempotencyKey,
    PaymentEnvironment Environment,
    string ExternalReference,
    decimal Amount,
    string Currency,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<PaymentOrderItem> Items);

public sealed record PaymentOrderItem(string Id, string Title, int Quantity, decimal UnitAmount);
