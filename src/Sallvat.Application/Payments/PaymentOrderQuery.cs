using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

// Expected values must come from the persisted attempt, never from a return URL or webhook body.
public sealed record PaymentOrderQuery(
    string ExternalOrderId, PaymentEnvironment Environment, string ExternalReference, decimal Amount, string Currency);

public enum PaymentOrderQueryStatus
{
    Found,
    Disabled,
    InvalidRequest,
    ConfigurationInvalid,
    AuthenticationFailure,
    NotFound,
    Unavailable,
    InvalidResponse,
}

public enum ObservedOrderState
{
    Created,
    Processed,
    ActionRequired,
    Other,
}

// Observation is deliberately not an approval command and contains no payer data or raw payload.
public sealed record PaymentOrderObservation(
    string ExternalOrderId,
    ObservedOrderState State,
    decimal PaidAmount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool HasTransactions);

public sealed record PaymentOrderQueryResult(PaymentOrderQueryStatus Status, PaymentOrderObservation? Observation = null);
