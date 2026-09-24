using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sallvat.Application.Carts;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal static class PaymentValidation
{
    internal static async Task<bool> OwnsOrderAsync(SallvatDbContext dbContext, Order order, CartOwner owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (owner.ApplicationUserId is Guid userId)
        {
            return userId != Guid.Empty && string.IsNullOrEmpty(owner.GuestToken)
                && await dbContext.Customers.AnyAsync(customer => customer.Id == order.CustomerId
                    && customer.ApplicationUserId == userId, cancellationToken);
        }

        if (owner.GuestToken is not { Length: 43 } token
            || !token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        // This authorizes only the current guest checkout session, not historical lookup by email.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return await dbContext.Carts.AnyAsync(cart => cart.Id == order.SourceCartId
                && cart.GuestTokenHash == hash && cart.CustomerId == null && cart.ExpiresAtUtc > now, cancellationToken)
            && !await dbContext.Customers.AnyAsync(customer => customer.Id == order.CustomerId
                && customer.ApplicationUserId != null, cancellationToken);
    }

    internal static bool HasValidSnapshots(Order order, List<OrderItem> items, List<StockReservation> reservations, DateTimeOffset now)
    {
        if (items.Count == 0 || items.Any(item => item.Currency != order.Currency)
            || items.Sum(item => item.UnitPrice * item.Quantity) != order.ItemsSubtotal
            || items.Sum(item => item.DiscountAmount) != order.DiscountTotal
            || items.Sum(item => item.Subtotal) + order.ShippingTotal != order.GrandTotal)
        {
            return false;
        }

        var quantities = items.GroupBy(item => item.ProductVariantId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Quantity));
        return reservations.Count == quantities.Count
            && reservations.Select(reservation => reservation.ProductVariantId).Distinct().Count() == reservations.Count
            && reservations.All(reservation => reservation.Status == StockReservationStatus.Reserved
                && reservation.ExpiresAtUtc > now && reservation.ExpiresAtUtc == order.ExpiresAtUtc
                && quantities.TryGetValue(reservation.ProductVariantId, out var quantity) && quantity == reservation.Quantity);
    }

    internal static bool IsConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        // The provider may wrap serialization failures in its execution strategy.
        || exception.GetBaseException() is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
