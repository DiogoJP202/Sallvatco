using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

public sealed record PaymentPreferenceRequest(
    Guid IdempotencyKey,
    PaymentEnvironment Environment,
    string ExternalReference,
    decimal Amount,
    string Currency,
    decimal ShippingTotal,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<PaymentPreferenceItem> Items);

// UnitAmount is the net amount after discount allocation, not the catalog price.
public sealed record PaymentPreferenceItem(
    string Id,
    string Title,
    int Quantity,
    decimal UnitAmount);
