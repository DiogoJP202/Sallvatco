using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sallvat.Application.Carts;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Promotions;

namespace Sallvat.Infrastructure.Orders;

internal sealed class OrderService(
    SallvatDbContext dbContext,
    IClock clock,
    IOptions<OrderOptions> options) : IOrderService
{
    private static readonly SemaphoreSlim CreationLock = new(1, 1);
    private static long inMemorySequence = 999;
    private const int GuestTokenLength = 43;

    public async Task<OrderCreationResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CheckoutAttemptId == Guid.Empty)
        {
            return Invalid("A tentativa de checkout é inválida.");
        }

        if (dbContext.Database.IsRelational())
        {
            return await CreateCoreAsync(request, cancellationToken);
        }

        // EF InMemory has no conditional SQL update or transaction isolation.
        // Serialize only that test/development provider so its concurrency
        // behavior remains deterministic; PostgreSQL uses database constraints.
        await CreationLock.WaitAsync(cancellationToken);
        try
        {
            return await CreateCoreAsync(request, cancellationToken);
        }
        finally
        {
            CreationLock.Release();
        }
    }

    private async Task<OrderCreationResult> CreateCoreAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await FindCartAsync(
            request.CartOwner,
            cancellationToken);
        if (cart is null)
        {
            return OrderCreationResult.Failure(
                OrderCreationStatus.NotFound,
                "A sacola não foi encontrada.");
        }

        var existing = await FindCreatedOrderAsync(
            request.CheckoutAttemptId,
            cart.Id,
            cancellationToken);
        if (existing is not null)
        {
            return OrderCreationResult.Success(
                existing,
                wasAlreadyCreated: true);
        }

        var now = clock.UtcNow;
        if (cart.ExpiresAtUtc <= now)
        {
            return OrderCreationResult.Failure(
                OrderCreationStatus.Unavailable,
                "A sacola expirou. Revise os itens antes de continuar.");
        }

        var shippingError = ValidateShipping(request.Shipping, now);
        if (shippingError is not null)
        {
            return Invalid(shippingError);
        }

        var (draft, validationErrors) = CheckoutDraftValidator.Validate(
            request.Checkout);
        if (draft is null)
        {
            return OrderCreationResult.Failure(
                OrderCreationStatus.Invalid,
                validationErrors.Select(error => error.Message).ToArray());
        }

        if (request.Checkout.Delivery.SavedAddressId is long addressId
            && !await OwnsAddressAsync(
                request.CartOwner.ApplicationUserId,
                addressId,
                cancellationToken))
        {
            return Invalid(
                "O endereço salvo não está disponível para esta conta.");
        }

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            existing = await FindCreatedOrderAsync(
                request.CheckoutAttemptId,
                cart.Id,
                cancellationToken);
            if (existing is not null)
            {
                await CommitAsync(transaction, cancellationToken);
                return OrderCreationResult.Success(
                    existing,
                    wasAlreadyCreated: true);
            }

            var lines = await LoadLinesAsync(cart.Id, cancellationToken);
            var lineError = ValidateLines(lines);
            if (lineError is not null)
            {
                return OrderCreationResult.Failure(
                    OrderCreationStatus.Unavailable,
                    lineError);
            }

            var couponResult = await BuildCouponAsync(
                cart,
                draft.Buyer.Email,
                lines,
                now,
                cancellationToken);
            if (couponResult.Error is not null)
            {
                return OrderCreationResult.Failure(
                    OrderCreationStatus.Unavailable,
                    couponResult.Error);
            }

            var allocations = couponResult.Quote?.Allocations
                .ToDictionary(allocation => allocation.LineId)
                ?? [];
            var amountLines = lines
                .Select(line => new OrderAmountLine(
                    line.CartItemId,
                    line.Quantity,
                    line.UnitPrice,
                    allocations.GetValueOrDefault(line.CartItemId)?.Amount
                        ?? 0m))
                .ToArray();
            var totals = OrderTotalsCalculator.Calculate(
                amountLines,
                request.Shipping.Price);
            var orderId = await NextOrderIdAsync(cancellationToken);
            var expiration = now.AddMinutes(
                options.Value.ReservationMinutes);
            var order = CreateOrder(
                orderId,
                request,
                cart,
                draft,
                couponResult.Coupon,
                totals,
                now,
                expiration);
            if (cart.CustomerId is null)
            {
                var guestCustomer = new Customer(
                    draft.Buyer.Name,
                    draft.Buyer.Email,
                    draft.Buyer.Phone,
                    now);
                order.AssignGuestCustomer(guestCustomer);
                dbContext.Customers.Add(guestCustomer);
            }

            dbContext.Orders.Add(order);
            dbContext.OrderAddresses.Add(CreateAddress(orderId, draft));

            foreach (var line in lines.OrderBy(line => line.ProductVariantId))
            {
                if (!await ReserveVariantAsync(
                    line,
                    now,
                    cancellationToken))
                {
                    await RollbackAsync(transaction, cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    return OrderCreationResult.Failure(
                        OrderCreationStatus.Unavailable,
                        $"A variante {line.Sku} não possui mais estoque suficiente.");
                }

                var discount = allocations
                    .GetValueOrDefault(line.CartItemId)?.Amount ?? 0m;
                dbContext.OrderItems.Add(new OrderItem(
                    orderId,
                    line.ProductVariantId,
                    line.ProductName,
                    $"{line.VolumeMl} ml",
                    line.Sku,
                    line.Quantity,
                    line.UnitPrice,
                    discount,
                    line.Currency));
                dbContext.StockReservations.Add(new StockReservation(
                    orderId,
                    line.ProductVariantId,
                    line.Quantity,
                    now,
                    expiration));
                dbContext.InventoryMovements.Add(new InventoryMovement(
                    line.ProductVariantId,
                    InventoryMovementType.Reservation,
                    line.Quantity,
                    line.OnHand,
                    line.Reserved + line.Quantity,
                    null,
                    $"Reserva do pedido {order.OrderNumber}",
                    now));
            }

            if (couponResult.Coupon is not null
                && couponResult.Quote is not null)
            {
                couponResult.Coupon.ClaimUsage(now);
                var redemption = new CouponRedemption(
                    couponResult.Coupon.Id,
                    request.CheckoutAttemptId,
                    cart.CustomerId,
                    draft.Buyer.Email,
                    couponResult.Quote.DiscountTotal,
                    now,
                    expiration);
                redemption.Consume(orderId, now);
                dbContext.CouponRedemptions.Add(redemption);
            }

            var cartItems = await dbContext.CartItems
                .Where(item => item.CartId == cart.Id)
                .ToListAsync(cancellationToken);
            dbContext.CartItems.RemoveRange(cartItems);
            cart.RemoveCoupon(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            return OrderCreationResult.Success(ToCreatedOrder(order));
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return OrderCreationResult.Failure(
                OrderCreationStatus.ConcurrencyConflict,
                "Preço, estoque ou cupom mudou. Revise e tente novamente.");
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            existing = await FindCreatedOrderAsync(
                request.CheckoutAttemptId,
                cart.Id,
                cancellationToken);
            return existing is null
                ? OrderCreationResult.Failure(
                    OrderCreationStatus.ConcurrencyConflict,
                    "Não foi possível confirmar o pedido. Tente novamente.")
                : OrderCreationResult.Success(
                    existing,
                    wasAlreadyCreated: true);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidOperationException)
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Invalid(exception.Message);
        }
    }

    private static Order CreateOrder(
        long orderId,
        CreateOrderRequest request,
        Cart cart,
        CheckoutDraft draft,
        Coupon? coupon,
        OrderTotals totals,
        DateTimeOffset now,
        DateTimeOffset expiration) =>
        new(
            orderId,
            $"SVT-{now:yyyyMMdd}-{orderId:D8}",
            request.CheckoutAttemptId,
            cart.Id,
            cart.CustomerId,
            draft.Buyer.Name,
            draft.Buyer.Email,
            draft.Buyer.Phone,
            totals.ItemsSubtotal,
            totals.DiscountTotal,
            totals.ShippingTotal,
            request.Shipping.Currency,
            coupon?.Id,
            coupon?.Code,
            request.Shipping.Provider,
            request.Shipping.Carrier,
            request.Shipping.Service,
            request.Shipping.QuoteId,
            request.Shipping.MinimumBusinessDays,
            request.Shipping.MaximumBusinessDays,
            request.Shipping.QuotedAtUtc,
            now,
            expiration);

    private static OrderAddress CreateAddress(
        long orderId,
        CheckoutDraft draft) =>
        new(
            orderId,
            draft.Delivery.RecipientName,
            draft.Delivery.PostalCode,
            draft.Delivery.Street,
            draft.Delivery.Number,
            draft.Delivery.Complement,
            draft.Delivery.District,
            draft.Delivery.City,
            draft.Delivery.StateCode,
            draft.Delivery.CountryCode);

    private async Task<CouponBuildResult> BuildCouponAsync(
        Cart cart,
        string buyerEmail,
        IReadOnlyCollection<OrderLine> lines,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (cart.CouponId is not long couponId)
        {
            return new(null, null, null);
        }

        var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
            item => item.Id == couponId,
            cancellationToken);
        if (coupon is null)
        {
            return new(null, null, "O cupom aplicado não está mais disponível.");
        }

        var normalizedEmail = buyerEmail.ToUpperInvariant();
        var statuses = new[]
        {
            CouponRedemptionStatus.Reserved,
            CouponRedemptionStatus.Consumed,
        };
        var identityUsage = await dbContext.CouponRedemptions.CountAsync(
            redemption => redemption.CouponId == coupon.Id
                && statuses.Contains(redemption.Status)
                && (cart.CustomerId.HasValue
                    ? redemption.CustomerId == cart.CustomerId
                        || redemption.NormalizedEmail == normalizedEmail
                    : redemption.CustomerId == null
                        && redemption.NormalizedEmail == normalizedEmail),
            cancellationToken);
        var quote = CouponDiscountCalculator.Calculate(
            coupon.DiscountType,
            coupon.Value,
            lines.Select(line => new CouponDiscountLine(
                line.CartItemId,
                line.UnitPrice * line.Quantity)).ToArray());
        var eligibility = coupon.Evaluate(
            quote.Subtotal,
            identityUsage,
            now);
        return eligibility == CouponEligibilityStatus.Eligible
            && quote.DiscountTotal > 0
                ? new(coupon, quote, null)
                : new(
                    null,
                    null,
                    CouponService.EligibilityMessage(
                        eligibility,
                        coupon.MinimumSubtotal));
    }

    private async Task<bool> ReserveVariantAsync(
        OrderLine line,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var newVersion = Guid.NewGuid();
            var affected = await dbContext.ProductVariants
                .Where(variant =>
                    variant.Id == line.ProductVariantId
                    && variant.ConcurrencyVersion
                        == line.VariantConcurrencyVersion
                    && variant.IsActive
                    && variant.OnHand - variant.Reserved >= line.Quantity
                    && dbContext.Products.Any(product =>
                        product.Id == variant.ProductId
                        && product.Status == ProductStatus.Published
                        && product.ConcurrencyVersion
                            == line.ProductConcurrencyVersion))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            variant => variant.Reserved,
                            variant => variant.Reserved + line.Quantity)
                        .SetProperty(
                            variant => variant.UpdatedAtUtc,
                            now)
                        .SetProperty(
                            variant => variant.ConcurrencyVersion,
                            newVersion),
                    cancellationToken);
            return affected == 1;
        }

        var variant = await dbContext.ProductVariants.SingleAsync(
            item => item.Id == line.ProductVariantId,
            cancellationToken);
        return variant.ConcurrencyVersion == line.VariantConcurrencyVersion
            && variant.Reserve(line.Quantity, now);
    }

    private Task<List<OrderLine>> LoadLinesAsync(
        Guid cartId,
        CancellationToken cancellationToken) =>
        (
            from item in dbContext.CartItems.AsNoTracking()
            join variant in dbContext.ProductVariants.AsNoTracking()
                on item.ProductVariantId equals variant.Id
            join product in dbContext.Products.AsNoTracking()
                on variant.ProductId equals product.Id
            where item.CartId == cartId
            orderby variant.Id
            select new OrderLine(
                item.Id,
                variant.Id,
                product.Name,
                product.Status,
                product.ConcurrencyVersion,
                variant.Sku,
                variant.VolumeMl,
                item.Quantity,
                variant.Price,
                variant.Currency,
                variant.IsActive,
                variant.OnHand,
                variant.Reserved,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                variant.ConcurrencyVersion))
        .ToListAsync(cancellationToken);

    private static string? ValidateLines(
        IReadOnlyCollection<OrderLine> lines)
    {
        if (lines.Count == 0)
        {
            return "A sacola está vazia.";
        }

        foreach (var line in lines)
        {
            if (line.ProductStatus != ProductStatus.Published
                || !line.VariantActive
                || line.UnitPrice <= 0
                || line.Currency != "BRL"
                || line.WeightKg <= 0
                || line.HeightCm <= 0
                || line.WidthCm <= 0
                || line.LengthCm <= 0)
            {
                return $"A variante {line.Sku} não está disponível para compra.";
            }

        }

        return null;
    }

    private static string? ValidateShipping(
        CheckoutShippingSnapshot shipping,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(shipping.Provider)
            || string.IsNullOrWhiteSpace(shipping.Carrier)
            || string.IsNullOrWhiteSpace(shipping.Service)
            || string.IsNullOrWhiteSpace(shipping.QuoteId)
            || shipping.Price <= 0
            || !string.Equals(
                shipping.Currency,
                "BRL",
                StringComparison.OrdinalIgnoreCase)
            || shipping.MinimumBusinessDays <= 0
            || shipping.MaximumBusinessDays
                < shipping.MinimumBusinessDays
            || shipping.QuotedAtUtc.Offset != TimeSpan.Zero
            || shipping.QuotedAtUtc > now
            || shipping.ExpiresAtUtc.Offset != TimeSpan.Zero
            || shipping.ExpiresAtUtc <= now)
        {
            return "A cotação de frete é inválida ou expirou.";
        }

        return null;
    }

    private async Task<Cart?> FindCartAsync(
        CartOwner owner,
        CancellationToken cancellationToken)
    {
        if (owner.ApplicationUserId is Guid applicationUserId
            && applicationUserId != Guid.Empty)
        {
            var customerId = await dbContext.Customers
                .Where(customer =>
                    customer.ApplicationUserId == applicationUserId)
                .Select(customer => (long?)customer.Id)
                .SingleOrDefaultAsync(cancellationToken);
            return customerId is null
                ? null
                : await dbContext.Carts.SingleOrDefaultAsync(
                    cart => cart.CustomerId == customerId,
                    cancellationToken);
        }

        if (!IsValidGuestToken(owner.GuestToken))
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(owner.GuestToken!)));
        return await dbContext.Carts.SingleOrDefaultAsync(
            cart => cart.GuestTokenHash == hash,
            cancellationToken);
    }

    private Task<bool> OwnsAddressAsync(
        Guid? applicationUserId,
        long addressId,
        CancellationToken cancellationToken) =>
        !applicationUserId.HasValue
            || applicationUserId.Value == Guid.Empty
            || addressId <= 0
                ? Task.FromResult(false)
                : (
                    from address in dbContext.Addresses.AsNoTracking()
                    join customer in dbContext.Customers.AsNoTracking()
                        on address.CustomerId equals customer.Id
                    where address.Id == addressId
                        && address.IsActive
                        && customer.ApplicationUserId
                            == applicationUserId.Value
                    select address.Id)
                .AnyAsync(cancellationToken);

    private Task<CreatedOrder?> FindCreatedOrderAsync(
        Guid checkoutAttemptId,
        Guid sourceCartId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.CheckoutAttemptId == checkoutAttemptId
                && order.SourceCartId == sourceCartId)
            .Select(order => new CreatedOrder(
                order.Id,
                order.OrderNumber,
                order.Status,
                order.GrandTotal,
                order.Currency,
                order.ExpiresAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<long> NextOrderIdAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return Interlocked.Increment(ref inMemorySequence);
        }

        return await dbContext.Database
            .SqlQueryRaw<long>(
                "SELECT nextval('order_number_sequence') AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static CreatedOrder ToCreatedOrder(Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.GrandTotal,
            order.Currency,
            order.ExpiresAtUtc);

    private static bool IsValidGuestToken(string? token) =>
        token is { Length: GuestTokenLength }
        && token.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_');

    private static OrderCreationResult Invalid(string message) =>
        OrderCreationResult.Failure(
            OrderCreationStatus.Invalid,
            message);

    private sealed record CouponBuildResult(
        Coupon? Coupon,
        CouponDiscountQuote? Quote,
        string? Error);

    private sealed record OrderLine(
        long CartItemId,
        long ProductVariantId,
        string ProductName,
        ProductStatus ProductStatus,
        Guid ProductConcurrencyVersion,
        string Sku,
        int VolumeMl,
        int Quantity,
        decimal UnitPrice,
        string Currency,
        bool VariantActive,
        int OnHand,
        int Reserved,
        decimal WeightKg,
        decimal HeightCm,
        decimal WidthCm,
        decimal LengthCm,
        Guid VariantConcurrencyVersion);
}
