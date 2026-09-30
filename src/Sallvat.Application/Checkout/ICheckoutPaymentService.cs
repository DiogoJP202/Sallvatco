using Sallvat.Application.Carts;
using Sallvat.Application.Orders;
using Sallvat.Application.Payments;
using Sallvat.Application.Shipping;

namespace Sallvat.Application.Checkout;

public interface ICheckoutPaymentService
{
    bool Enabled { get; }
    Task<OrderCreationResult> ConfirmAsync(CheckoutConfirmation confirmation, CartOwner owner, CancellationToken cancellationToken = default);
    Task<CheckoutPaymentSummary?> GetAsync(Guid attemptId, CartOwner owner, CancellationToken cancellationToken = default);
    Task<PaymentDispatchResult> ContinueAsync(Guid attemptId, CartOwner owner, CancellationToken cancellationToken = default);
}

public sealed record CheckoutConfirmation(
    Guid AttemptId,
    CheckoutDraftInput Checkout,
    FreightQuoteOption Freight,
    OrderReviewExpectation Review,
    DateTimeOffset ExpiresAtUtc);

public enum CheckoutPaymentState
{
    AwaitingPayment,
    Confirmed,
    RequiresAttention,
    Closed,
    Refunded,
}

public sealed record CheckoutPaymentSummary(
    Guid AttemptId,
    string OrderNumber,
    decimal Total,
    string Currency,
    DateTimeOffset ExpiresAtUtc,
    CheckoutPaymentState State,
    bool CanContinue);
