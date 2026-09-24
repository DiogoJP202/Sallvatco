using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Application.Payments;
using Sallvat.Application.Time;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal sealed class PaymentDispatchService(
    SallvatDbContext db, IPaymentGateway gateway, IOptions<MercadoPagoOptions> configuredOptions, IClock clock) : IPaymentDispatchService
{
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);

    public async Task<PaymentDispatchResult> DispatchAsync(long paymentId, CartOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuredOptions.Value;
        if (!options.OrdersEnabled || !MercadoPagoOptions.IsValid(options))
        {
            return new(PaymentDispatchStatus.Disabled);
        }

        if (paymentId <= 0)
        {
            return new(PaymentDispatchStatus.Invalid);
        }

        if (db.ChangeTracker.HasChanges())
        {
            return new(PaymentDispatchStatus.Conflict);
        }

        var claim = await ClaimAsync(paymentId, owner, cancellationToken);
        if (claim.Result is not null)
        {
            return claim.Result;
        }

        // The committed claim is never reclaimed automatically, even if this process dies before HTTP.
        // After commit, browser cancellation cannot abandon persistence of the provider's result.
        using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds + 5));
        PaymentOrderResult response;
        try
        {
            response = await CanSendAsync(paymentId, owner, claim.Token, sendTimeout.Token)
                ? await gateway.CreateOrderAsync(claim.Request!, sendTimeout.Token)
                : new(PaymentOrderStatus.OutcomeUnknown);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or NpgsqlException)
        {
            response = new(PaymentOrderStatus.OutcomeUnknown);
        }

        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            return await FinishAsync(paymentId, claim.Token, response, saveTimeout.Token);
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or OperationCanceledException
            || PaymentValidation.IsConflict(exception))
        {
            // The durable Sending claim blocks another POST. Reconciliation is required, not a new key.
            db.ChangeTracker.Clear();
            return new(PaymentDispatchStatus.RequiresAttention);
        }
    }

    private async Task<Claim> ClaimAsync(long paymentId, CartOwner owner, CancellationToken cancellationToken)
    {
        var relational = db.Database.IsRelational();
        if (!relational)
        {
            await InMemoryLock.WaitAsync(cancellationToken);
        }

        try
        {
            await using var transaction = relational
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            try
            {
                db.ChangeTracker.Clear();
                var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
                var order = payment is null ? null : await db.Orders.SingleOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);
                var now = clock.UtcNow;
                if (order is null || payment is null || !await PaymentValidation.OwnsOrderAsync(db, order, owner, now, cancellationToken))
                {
                    return new(new(PaymentDispatchStatus.NotFound));
                }

                if (!Matches(payment, order) || !await IsPayableAsync(order, now, cancellationToken))
                {
                    return new(new(PaymentDispatchStatus.Invalid));
                }

                if (payment.DispatchState == PaymentDispatchState.Completed && payment.Status == PaymentStatus.Pending && payment.ExternalOrderId is not null)
                {
                    return new(new(PaymentDispatchStatus.Ready, CheckoutUrl(payment.ExternalOrderId)));
                }

                if (payment.DispatchState != PaymentDispatchState.NotStarted || payment.Status != PaymentStatus.Created || payment.PreferenceId is not null)
                {
                    return new(new(PaymentDispatchStatus.RequiresAttention));
                }

                var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).OrderBy(i => i.Id).ToListAsync(cancellationToken);
                var request = BuildRequest(payment, order, items);
                if (request is null || order.ExpiresAtUtc - now < TimeSpan.FromSeconds(1)
                    || order.ExpiresAtUtc - now > TimeSpan.FromDays(1) || now < payment.UpdatedAtUtc)
                {
                    return new(new(PaymentDispatchStatus.Invalid));
                }

                var token = Guid.NewGuid();
                if (!payment.TryBeginOrderDispatch(token, now))
                {
                    return new(new(PaymentDispatchStatus.RequiresAttention));
                }

                order.RegisterPaymentAttempt(now);
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new(null, token, request);
            }
            catch (Exception exception) when (PaymentValidation.IsConflict(exception))
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }

                db.ChangeTracker.Clear();
                return new(new(PaymentDispatchStatus.Conflict));
            }
        }
        finally
        {
            if (!relational)
            {
                InMemoryLock.Release();
            }
        }
    }

    private async Task<bool> CanSendAsync(long paymentId, CartOwner owner, Guid token, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, cancellationToken);
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
        return payment.DispatchToken == token && payment.DispatchState == PaymentDispatchState.Sending
            && Matches(payment, order) && await IsPayableAsync(order, clock.UtcNow, cancellationToken)
            && await PaymentValidation.OwnsOrderAsync(db, order, owner, clock.UtcNow, cancellationToken);
    }

    private async Task<PaymentDispatchResult> FinishAsync(long paymentId, Guid token, PaymentOrderResult response, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        db.ChangeTracker.Clear();
        var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
        var order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
        if (payment.DispatchToken != token || payment.DispatchState != PaymentDispatchState.Sending)
        {
            return new(PaymentDispatchStatus.RequiresAttention);
        }

        var now = clock.UtcNow;
        var payable = Matches(payment, order) && await IsPayableAsync(order, now, cancellationToken);
        var externalId = ValidCreatedResponse(response) ? response.ExternalOrderId : null;
        payable = payable && response.Status != PaymentOrderStatus.CreatedAfterExpiry;
        payment.CompleteOrderDispatch(token, externalId, payable, now < payment.UpdatedAtUtc ? payment.UpdatedAtUtc : now);
        if (payable)
        {
            // Compete with cancellation/expiration; never mark a cancelled order payable again.
            order.RegisterPaymentAttempt(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return payment.DispatchState == PaymentDispatchState.Completed
            ? new(PaymentDispatchStatus.Ready, CheckoutUrl(payment.ExternalOrderId!))
            : new(PaymentDispatchStatus.RequiresAttention);
    }

    private async Task<bool> IsPayableAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken) =>
        order.Status == OrderStatus.PendingPayment && order.ExpiresAtUtc > now && order.UpdatedAtUtc <= now
        && PaymentValidation.HasValidSnapshots(order,
            await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).ToListAsync(cancellationToken),
            await db.StockReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(cancellationToken), now);

    private static bool Matches(Payment payment, Order order) =>
        payment.Environment == PaymentEnvironment.Sandbox && payment.Amount == order.GrandTotal
        && payment.Currency == order.Currency && payment.ExternalReference == order.OrderNumber && payment.ExpiresAtUtc == order.ExpiresAtUtc;

    internal static PaymentOrderRequest? BuildRequest(Payment payment, Order order, IReadOnlyList<OrderItem> items)
    {
        var lines = new List<PaymentOrderItem>();
        foreach (var item in items)
        {
            // Divide in integer cents, conserving the exact snapshot discount, even for indivisible quantities.
            var cents = item.Subtotal * 100m;
            var baseCents = decimal.Floor(cents / item.Quantity);
            var extraUnits = (int)(cents - baseCents * item.Quantity);
            if (baseCents < 1 || item.Quantity > 1_000)
            {
                return null; // Free units need an explicitly homologated provider representation.
            }

            var id = item.Id.ToString(CultureInfo.InvariantCulture);
            var title = item.ProductName + " — " + item.VariantName;
            if (title.Length > 256 || title.Any(char.IsControl))
            {
                return null;
            }

            if (item.Quantity > extraUnits)
            {
                lines.Add(new("item-" + id, title, item.Quantity - extraUnits, baseCents / 100m));
            }

            if (extraUnits > 0)
            {
                lines.Add(new("item-" + id + "-remainder", title, extraUnits, (baseCents + 1m) / 100m));
            }
        }

        if (order.ShippingTotal > 0)
        {
            lines.Add(new("shipping", "Frete", 1, order.ShippingTotal));
        }

        return lines.Count is > 0 and <= 100 && lines.Sum(line => line.UnitAmount * line.Quantity) == payment.Amount
            ? new(payment.IdempotencyKey, payment.Environment, payment.ExternalReference, payment.Amount,
                payment.Currency, payment.ExpiresAtUtc, lines) : null;
    }

    private static bool ValidCreatedResponse(PaymentOrderResult response) =>
        response.Status is PaymentOrderStatus.Created or PaymentOrderStatus.CreatedAfterExpiry
        && response.ExternalOrderId is { Length: <= Payment.ExternalOrderIdMaxLength } id
        && id.StartsWith("ORD", StringComparison.Ordinal)
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        && (response.Status == PaymentOrderStatus.CreatedAfterExpiry ? response.CheckoutUrl is null : response.CheckoutUrl == CheckoutUrl(id));

    private static Uri CheckoutUrl(string id) => new("https://www.mercadopago.com.br/checkout/v1/redirect?order_id=" + Uri.EscapeDataString(id));

    private sealed record Claim(PaymentDispatchResult? Result, Guid Token = default, PaymentOrderRequest? Request = null);
}
