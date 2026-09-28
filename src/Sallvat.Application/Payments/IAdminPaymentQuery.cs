using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.Application.Payments;

// Internal read model. Every HTTP consumer must require the Admin policy.
public interface IAdminPaymentQuery
{
    Task<AdminPaymentPage> ListAsync(AdminPaymentFilter filter, long? beforeId = null, CancellationToken cancellationToken = default);
    Task<AdminPaymentDetails?> FindAsync(long id, CancellationToken cancellationToken = default);
}

public enum AdminPaymentFilter { Attention, All, Pending, Approved }

public sealed record AdminPaymentSummary(
    long Id, string OrderNumber, OrderStatus OrderStatus, PaymentEnvironment Environment,
    PaymentStatus Status, PaymentDispatchState DispatchState, PaymentAttentionReason? AttentionReason,
    decimal Amount, string Currency, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record AdminPaymentPage(AdminPaymentFilter Filter, IReadOnlyList<AdminPaymentSummary> Items, long? NextBeforeId);

public sealed record AdminPaymentReceipt(DateTimeOffset ReceivedAtUtc, WebhookOutcome Outcome);

public sealed record AdminPaymentDetails(
    AdminPaymentSummary Payment, string? ExternalOrderId, string? ExternalPaymentId,
    DateTimeOffset? DispatchStartedAtUtc, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc, DateTimeOffset? ProviderUpdatedAtUtc,
    IReadOnlyList<AdminPaymentReceipt> Receipts, bool HasOlderReceipts);
