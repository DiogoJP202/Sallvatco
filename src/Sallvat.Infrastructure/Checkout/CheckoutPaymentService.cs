using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Payments;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Shipping;

namespace Sallvat.Infrastructure.Checkout;

internal sealed class CheckoutPaymentService(
    SallvatDbContext dbContext,
    ICheckoutService checkout,
    IOrderService orders,
    IPaymentPreparationService preparation,
    IPaymentDispatchService dispatch,
    IOptions<MercadoPagoOptions> paymentOptions,
    IOptions<MelhorEnvioOptions> freightOptions,
    IClock clock) : ICheckoutPaymentService
{
    public bool Enabled => paymentOptions.Value.CheckoutEnabled
        && MercadoPagoOptions.IsValid(paymentOptions.Value)
        && freightOptions.Value.Enabled && MelhorEnvioOptions.IsValid(freightOptions.Value)
        && freightOptions.Value.BaseUrl.TrimEnd('/') == "https://sandbox.melhorenvio.com.br";

    public async Task<OrderCreationResult> ConfirmAsync(
        CheckoutConfirmation confirmation, CartOwner owner, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return Invalid("Checkout de teste desativado.");
        }

        // A replay of this attempt is safe even after the first request emptied the cart.
        var existing = await FindOwnedAsync(confirmation.AttemptId, owner, cancellationToken);
        if (existing is not null)
        {
            return OrderCreationResult.Success(new(existing.Id, existing.OrderNumber, existing.Status,
                existing.GrandTotal, existing.Currency, existing.ExpiresAtUtc), wasAlreadyCreated: true);
        }

        if (confirmation.AttemptId == Guid.Empty || confirmation.ExpiresAtUtc <= clock.UtcNow
            || confirmation.Freight.ExpiresAtUtc <= clock.UtcNow)
        {
            return Invalid("A revisão expirou. Revise os dados e o frete novamente.");
        }

        var validation = await checkout.ValidateAsync(owner, owner.ApplicationUserId, confirmation.Checkout, cancellationToken);
        if (!validation.Succeeded)
        {
            return Invalid("A sacola ou os dados mudaram. Revise antes de continuar.");
        }

        var freight = await checkout.RevalidateFreightAsync(owner, validation.Draft!.Delivery.PostalCode,
            confirmation.Freight.QuoteId, confirmation.Freight.Price, cancellationToken);
        var selected = confirmation.Freight;
        if (!freight.Succeeded || freight.Snapshot is not { } current
            || current.Currency != selected.Currency || current.Carrier != selected.Carrier
            || current.Service != selected.Service || current.MinimumBusinessDays != selected.MinimumBusinessDays
            || current.MaximumBusinessDays != selected.MaximumBusinessDays)
        {
            return Invalid("O frete mudou ou está indisponível. Consulte e confirme uma nova cotação.");
        }

        return await orders.CreateAsync(new(confirmation.AttemptId, owner, confirmation.Checkout,
            current, confirmation.Review), cancellationToken);
    }

    public async Task<CheckoutPaymentSummary?> GetAsync(
        Guid attemptId, CartOwner owner, CancellationToken cancellationToken = default)
    {
        var order = await FindOwnedAsync(attemptId, owner, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var payments = await dbContext.Payments.AsNoTracking().Where(payment => payment.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var attention = order.Status == OrderStatus.RequiresAttention
            || payments.Any(payment => payment.Status == PaymentStatus.RequiresAttention
                || payment.DispatchState is PaymentDispatchState.Sending or PaymentDispatchState.RequiresAttention
                || (payment.Status == PaymentStatus.Approved && order.Status is OrderStatus.PendingPayment or OrderStatus.Cancelled));
        var state = attention ? CheckoutPaymentState.RequiresAttention
            : order.Status == OrderStatus.Refunded ? CheckoutPaymentState.Refunded
            : order.Status is OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered
                ? CheckoutPaymentState.Confirmed
            : order.Status == OrderStatus.Cancelled || order.ExpiresAtUtc <= clock.UtcNow
                || payments.Any(payment => payment.Status is PaymentStatus.Rejected or PaymentStatus.Cancelled or PaymentStatus.Expired)
                ? CheckoutPaymentState.Closed
            : CheckoutPaymentState.AwaitingPayment;
        var canContinue = Enabled && state == CheckoutPaymentState.AwaitingPayment
            && (payments.Count == 0 || payments.Count == 1
                && payments[0].Environment == PaymentEnvironment.Sandbox
                && ((payments[0].Status == PaymentStatus.Created && payments[0].DispatchState == PaymentDispatchState.NotStarted)
                    || (payments[0].Status == PaymentStatus.Pending && payments[0].DispatchState == PaymentDispatchState.Completed)));
        return new(attemptId, order.OrderNumber, order.GrandTotal, order.Currency, order.ExpiresAtUtc, state, canContinue);
    }

    public async Task<PaymentDispatchResult> ContinueAsync(
        Guid attemptId, CartOwner owner, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return new(PaymentDispatchStatus.Disabled);
        }

        var summary = await GetAsync(attemptId, owner, cancellationToken);
        if (summary is null)
        {
            return new(PaymentDispatchStatus.NotFound);
        }

        if (!summary.CanContinue)
        {
            return new(PaymentDispatchStatus.Invalid);
        }

        var order = await FindOwnedAsync(attemptId, owner, cancellationToken);
        if (order is null)
        {
            return new(PaymentDispatchStatus.NotFound);
        }

        var prepared = await preparation.PrepareAsync(order.Id, owner, PaymentEnvironment.Sandbox, cancellationToken);
        if (prepared.Status is not (PaymentPreparationStatus.Prepared or PaymentPreparationStatus.AlreadyPrepared)
            || prepared.PaymentId is not long paymentId)
        {
            return new(PaymentDispatchStatus.RequiresAttention);
        }

        return await dispatch.DispatchAsync(paymentId, owner, cancellationToken);
    }

    private async Task<Order?> FindOwnedAsync(Guid attemptId, CartOwner owner, CancellationToken cancellationToken)
    {
        if (attemptId == Guid.Empty)
        {
            return null;
        }

        var order = await dbContext.Orders.AsNoTracking()
            .SingleOrDefaultAsync(order => order.CheckoutAttemptId == attemptId, cancellationToken);
        return order is not null && await PaymentValidation.OwnsOrderAsync(dbContext, order, owner, clock.UtcNow, cancellationToken)
            ? order : null;
    }

    private static OrderCreationResult Invalid(string message) => OrderCreationResult.Failure(OrderCreationStatus.Invalid, message);
}
