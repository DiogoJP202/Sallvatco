using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Carts;
using Sallvat.Application.Catalog;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Promotions;

namespace Sallvat.Infrastructure.Carts;

internal sealed class CartService(
    SallvatDbContext dbContext,
    IClock clock,
    IImageStorage imageStorage) : ICartService
{
    private static readonly TimeSpan CartLifetime = TimeSpan.FromDays(30);
    private const int GuestTokenLength = 43;

    public async Task<CartSummary> GetAsync(
        CartOwner owner,
        CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);

        return cart is null
            ? CartSummary.Empty
            : await BuildSummaryAsync(cart, cancellationToken);
    }

    public async Task<CartMutationResult> AddItemAsync(
        CartOwner owner,
        long variantId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidQuantity(quantity))
        {
            return InvalidQuantity();
        }

        var availability = await FindVariantAsync(
            variantId,
            cancellationToken);
        if (availability is null)
        {
            return NotFound();
        }

        if (!availability.IsSellable)
        {
            return Unavailable(
                "Esta variante não está disponível para compra.");
        }

        var cart = await FindCartAsync(
            owner,
            reactivateExpired: true,
            cancellationToken);
        if (cart is null)
        {
            cart = await CreateCartAsync(owner, cancellationToken);
        }

        if (cart is null)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.Invalid,
                "Não foi possível identificar o carrinho.");
        }

        var item = await dbContext.CartItems.SingleOrDefaultAsync(
            candidate => candidate.CartId == cart.Id
                && candidate.ProductVariantId == variantId,
            cancellationToken);
        var requestedQuantity = item is null
            ? quantity
            : item.Quantity + quantity;
        if (!IsValidQuantity(requestedQuantity))
        {
            return InvalidQuantity();
        }

        if (requestedQuantity > availability.Available)
        {
            return Unavailable(StockMessage(availability.Available));
        }

        var now = clock.UtcNow;
        if (item is null)
        {
            dbContext.CartItems.Add(new CartItem(
                cart.Id,
                availability.Variant.Id,
                requestedQuantity,
                availability.Variant.Price,
                now,
                availability.Variant.Currency));
        }
        else
        {
            item.ChangeQuantity(requestedQuantity, now);
        }

        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CartMutationResult.Success();
    }

    public async Task<CartMutationResult> UpdateItemAsync(
        CartOwner owner,
        long itemId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidQuantity(quantity))
        {
            return InvalidQuantity();
        }

        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);
        if (cart is null)
        {
            return NotFound();
        }

        var item = await dbContext.CartItems.SingleOrDefaultAsync(
            candidate => candidate.Id == itemId
                && candidate.CartId == cart.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var availability = await FindVariantAsync(
            item.ProductVariantId,
            cancellationToken);
        if (availability is null || !availability.IsSellable)
        {
            return Unavailable(
                "Esta variante não está mais disponível para compra.");
        }

        if (quantity > availability.Available)
        {
            return Unavailable(StockMessage(availability.Available));
        }

        var now = clock.UtcNow;
        item.ChangeQuantity(quantity, now);
        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CartMutationResult.Success();
    }

    public async Task<CartMutationResult> RemoveItemAsync(
        CartOwner owner,
        long itemId,
        CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);
        if (cart is null)
        {
            return NotFound();
        }

        var item = await dbContext.CartItems.SingleOrDefaultAsync(
            candidate => candidate.Id == itemId
                && candidate.CartId == cart.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        dbContext.CartItems.Remove(item);
        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CartMutationResult.Success();
    }

    public async Task<CartMutationResult> ClearAsync(
        CartOwner owner,
        CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);
        if (cart is null)
        {
            return CartMutationResult.Success();
        }

        var items = await dbContext.CartItems
            .Where(item => item.CartId == cart.Id)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        dbContext.CartItems.RemoveRange(items);
        cart.RemoveCoupon(now);
        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CartMutationResult.Success();
    }

    public async Task<CartMutationResult> ApplyCouponAsync(
        CartOwner owner,
        string code,
        CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);
        if (cart is null)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.Invalid,
                "Adicione um produto antes de aplicar o cupom.");
        }

        string normalizedCode;
        try
        {
            normalizedCode = CouponCode.Normalize(code);
        }
        catch (ArgumentException)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.Invalid,
                "Informe um código de cupom válido.");
        }

        var coupon = await dbContext.Coupons.SingleOrDefaultAsync(
            candidate => candidate.NormalizedCode == normalizedCode,
            cancellationToken);
        if (coupon is null)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.NotFound,
                "Cupom não encontrado.");
        }

        var subtotal = await CartSubtotalAsync(cart.Id, cancellationToken);
        if (subtotal <= 0)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.Invalid,
                "Adicione um produto antes de aplicar o cupom.");
        }

        var identityUsage = cart.CustomerId.HasValue
            ? await ActiveIdentityUsageAsync(
                coupon.Id,
                cart.CustomerId.Value,
                cancellationToken)
            : 0;
        var eligibility = coupon.Evaluate(
            subtotal,
            identityUsage,
            clock.UtcNow);
        if (eligibility != CouponEligibilityStatus.Eligible)
        {
            return CartMutationResult.Failure(
                CartMutationStatus.Unavailable,
                CouponService.EligibilityMessage(
                    eligibility,
                    coupon.MinimumSubtotal));
        }

        var now = clock.UtcNow;
        cart.ApplyCoupon(coupon.Id, now);
        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartMutationResult.Success();
    }

    public async Task<CartMutationResult> RemoveCouponAsync(
        CartOwner owner,
        CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(
            owner,
            reactivateExpired: false,
            cancellationToken);
        if (cart is null)
        {
            return CartMutationResult.Success();
        }

        var now = clock.UtcNow;
        cart.RemoveCoupon(now);
        cart.Refresh(now, now.Add(CartLifetime));
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartMutationResult.Success();
    }

    public async Task MergeGuestCartAsync(
        string guestToken,
        Guid applicationUserId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidGuestToken(guestToken)
            || applicationUserId == Guid.Empty)
        {
            return;
        }

        var now = clock.UtcNow;
        var guestHash = HashToken(guestToken);
        var guestCart = await dbContext.Carts.SingleOrDefaultAsync(
            cart => cart.GuestTokenHash == guestHash,
            cancellationToken);
        if (guestCart is null || guestCart.ExpiresAtUtc <= now)
        {
            return;
        }

        var customerId = await FindCustomerIdAsync(
            applicationUserId,
            cancellationToken);
        if (customerId is null)
        {
            return;
        }

        var customerCart = await dbContext.Carts.SingleOrDefaultAsync(
            cart => cart.CustomerId == customerId,
            cancellationToken);
        var customerCartWasExpired = false;
        if (customerCart is null)
        {
            customerCart = Cart.CreateForCustomer(
                customerId.Value,
                now,
                now.Add(CartLifetime));
            dbContext.Carts.Add(customerCart);
        }
        else if (customerCart.ExpiresAtUtc <= now)
        {
            await RemoveItemsAsync(customerCart.Id, cancellationToken);
            customerCart.RemoveCoupon(now);
            customerCartWasExpired = true;
        }

        var customerItems = customerCartWasExpired
            ? []
            : await dbContext.CartItems
                .Where(item => item.CartId == customerCart.Id)
                .ToDictionaryAsync(
                    item => item.ProductVariantId,
                    cancellationToken);
        var guestItems = await dbContext.CartItems
            .Where(item => item.CartId == guestCart.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var variantIds = customerItems.Keys
            .Concat(guestItems.Select(item => item.ProductVariantId))
            .Distinct()
            .ToArray();
        var availableByVariant = await dbContext.ProductVariants
            .Where(variant => variantIds.Contains(variant.Id))
            .ToDictionaryAsync(
                variant => variant.Id,
                variant => variant.OnHand - variant.Reserved,
                cancellationToken);

        foreach (var guestItem in guestItems)
        {
            if (customerItems.TryGetValue(
                    guestItem.ProductVariantId,
                    out var customerItem))
            {
                var mergedQuantity = Math.Min(
                    CartItem.MaximumQuantity,
                    customerItem.Quantity + guestItem.Quantity);
                if (availableByVariant.TryGetValue(
                        guestItem.ProductVariantId,
                        out var available)
                    && available > 0)
                {
                    mergedQuantity = Math.Min(mergedQuantity, available);
                }

                customerItem.ChangeQuantity(mergedQuantity, now);
                dbContext.CartItems.Remove(guestItem);
            }
            else
            {
                guestItem.MoveToCart(customerCart.Id, now);
            }
        }


        if (customerCart.CouponId is null && guestCart.CouponId.HasValue)
        {
            customerCart.ApplyCoupon(guestCart.CouponId.Value, now);
        }

        customerCart.Refresh(now, now.Add(CartLifetime));
        dbContext.Carts.Remove(guestCart);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        var expiredCarts = await dbContext.Carts
            .Where(cart => cart.ExpiresAtUtc <= clock.UtcNow)
            .OrderBy(cart => cart.ExpiresAtUtc)
            .Take(maximumItems)
            .ToListAsync(cancellationToken);
        if (expiredCarts.Count == 0)
        {
            return 0;
        }

        var cartIds = expiredCarts.Select(cart => cart.Id).ToArray();
        var expiredItems = await dbContext.CartItems
            .Where(item => cartIds.Contains(item.CartId))
            .ToListAsync(cancellationToken);
        dbContext.CartItems.RemoveRange(expiredItems);
        dbContext.Carts.RemoveRange(expiredCarts);
        await dbContext.SaveChangesAsync(cancellationToken);

        return expiredCarts.Count;
    }

    private async Task<CartSummary> BuildSummaryAsync(
        Cart cart,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from item in dbContext.CartItems.AsNoTracking()
            join variant in dbContext.ProductVariants.AsNoTracking()
                on item.ProductVariantId equals variant.Id
            join product in dbContext.Products.AsNoTracking()
                on variant.ProductId equals product.Id
            let cover = dbContext.ProductImages.AsNoTracking()
                .Where(image => image.ProductId == product.Id && image.IsCover)
                .Select(image => new
                {
                    image.StorageKey,
                    image.AltText,
                    image.Width,
                    image.Height,
                })
                .FirstOrDefault()
            where item.CartId == cart.Id
            orderby item.CreatedAtUtc, item.Id
            select new CartRow(
                item.Id,
                variant.Id,
                product.Name,
                product.Slug,
                product.Status,
                variant.VolumeMl,
                variant.Sku,
                item.Quantity,
                variant.Price,
                item.ReferenceUnitPrice,
                variant.Currency,
                variant.OnHand - variant.Reserved,
                variant.IsActive,
                variant.WeightKg,
                variant.HeightCm,
                variant.WidthCm,
                variant.LengthCm,
                cover == null ? null : cover.StorageKey,
                cover == null ? null : cover.AltText,
                cover == null ? null : cover.Width,
                cover == null ? null : cover.Height))
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToCartLine).ToArray();
        var subtotal = items.Sum(item => item.LineTotal);
        var cartCoupon = await BuildCouponAsync(
            cart,
            items,
            subtotal,
            cancellationToken);
        return new CartSummary(
            items,
            subtotal,
            "BRL",
            cart.ExpiresAtUtc,
            cartCoupon);
    }

    private async Task<CartCoupon?> BuildCouponAsync(
        Cart cart,
        IReadOnlyList<CartLine> items,
        decimal subtotal,
        CancellationToken cancellationToken)
    {
        if (cart.CouponId is not long couponId)
        {
            return null;
        }

        var coupon = await dbContext.Coupons
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == couponId,
                cancellationToken);
        if (coupon is null)
        {
            return null;
        }

        var identityUsage = cart.CustomerId.HasValue
            ? await ActiveIdentityUsageAsync(
                coupon.Id,
                cart.CustomerId.Value,
                cancellationToken)
            : 0;
        var eligibility = coupon.Evaluate(
            subtotal,
            identityUsage,
            clock.UtcNow);
        if (eligibility != CouponEligibilityStatus.Eligible)
        {
            return new CartCoupon(
                coupon.Id,
                coupon.Code,
                0,
                false,
                CouponService.EligibilityMessage(
                    eligibility,
                    coupon.MinimumSubtotal));
        }

        var quote = CouponDiscountCalculator.Calculate(
            coupon.DiscountType,
            coupon.Value,
            items.Select(item => new CouponDiscountLine(
                item.ItemId,
                item.LineTotal)).ToArray());
        return new CartCoupon(
            coupon.Id,
            coupon.Code,
            quote.DiscountTotal,
            true,
            null);
    }

    private Task<decimal> CartSubtotalAsync(
        Guid cartId,
        CancellationToken cancellationToken) =>
        (from item in dbContext.CartItems
         join variant in dbContext.ProductVariants
             on item.ProductVariantId equals variant.Id
         where item.CartId == cartId
         select item.Quantity * variant.Price)
        .SumAsync(cancellationToken);

    private Task<int> ActiveIdentityUsageAsync(
        long couponId,
        long customerId,
        CancellationToken cancellationToken) =>
        dbContext.CouponRedemptions.CountAsync(
            redemption => redemption.CouponId == couponId
                && redemption.CustomerId == customerId
                && redemption.Status != CouponRedemptionStatus.Released,
            cancellationToken);

    private CartLine ToCartLine(CartRow row)
    {
        var sellable = row.Status == ProductStatus.Published
            && row.IsActive
            && row.UnitPrice > 0
            && row.WeightKg > 0
            && row.HeightCm > 0
            && row.WidthCm > 0
            && row.LengthCm > 0;
        var image = row.StorageKey is null
            || row.AltText is null
            || row.Width is null
            || row.Height is null
                ? null
                : new CartImage(
                    imageStorage.GetPublicUrl(
                        $"{row.StorageKey}/thumb.webp"),
                    row.AltText,
                    row.Width.Value,
                    row.Height.Value);

        return new CartLine(
            row.ItemId,
            row.VariantId,
            row.ProductName,
            row.ProductSlug,
            row.VolumeMl,
            row.Sku,
            row.Quantity,
            row.UnitPrice,
            row.ReferenceUnitPrice,
            row.Currency,
            row.Available,
            sellable && row.Available >= row.Quantity,
            image);
    }

    private async Task<Cart?> FindCartAsync(
        CartOwner owner,
        bool reactivateExpired,
        CancellationToken cancellationToken)
    {
        Cart? cart;
        if (owner.ApplicationUserId is Guid applicationUserId
            && applicationUserId != Guid.Empty)
        {
            var customerId = await FindCustomerIdAsync(
                applicationUserId,
                cancellationToken);
            cart = customerId is null
                ? null
                : await dbContext.Carts.SingleOrDefaultAsync(
                    candidate => candidate.CustomerId == customerId,
                    cancellationToken);
        }
        else if (IsValidGuestToken(owner.GuestToken))
        {
            var hash = HashToken(owner.GuestToken!);
            cart = await dbContext.Carts.SingleOrDefaultAsync(
                candidate => candidate.GuestTokenHash == hash,
                cancellationToken);
        }
        else
        {
            return null;
        }

        if (cart is null || cart.ExpiresAtUtc > clock.UtcNow)
        {
            return cart;
        }

        if (!reactivateExpired)
        {
            return null;
        }

        await RemoveItemsAsync(cart.Id, cancellationToken);
        var now = clock.UtcNow;
        cart.RemoveCoupon(now);
        cart.Refresh(now, now.Add(CartLifetime));
        return cart;
    }

    private async Task<Cart?> CreateCartAsync(
        CartOwner owner,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        Cart? cart = null;
        if (owner.ApplicationUserId is Guid applicationUserId
            && applicationUserId != Guid.Empty)
        {
            var customerId = await FindCustomerIdAsync(
                applicationUserId,
                cancellationToken);
            if (customerId is not null)
            {
                cart = Cart.CreateForCustomer(
                    customerId.Value,
                    now,
                    now.Add(CartLifetime));
            }
        }
        else if (IsValidGuestToken(owner.GuestToken))
        {
            cart = Cart.CreateGuest(
                HashToken(owner.GuestToken!),
                now,
                now.Add(CartLifetime));
        }

        if (cart is not null)
        {
            dbContext.Carts.Add(cart);
        }

        return cart;
    }

    private Task<long?> FindCustomerIdAsync(
        Guid applicationUserId,
        CancellationToken cancellationToken) =>
        dbContext.Customers
            .Where(customer =>
                customer.ApplicationUserId == applicationUserId)
            .Select(customer => (long?)customer.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<VariantAvailability?> FindVariantAsync(
        long variantId,
        CancellationToken cancellationToken) =>
        await (
            from variant in dbContext.ProductVariants
            join product in dbContext.Products
                on variant.ProductId equals product.Id
            where variant.Id == variantId
            select new VariantAvailability(
                variant,
                product.Status,
                variant.OnHand - variant.Reserved))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task RemoveItemsAsync(
        Guid cartId,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.CartItems
            .Where(item => item.CartId == cartId)
            .ToListAsync(cancellationToken);
        dbContext.CartItems.RemoveRange(items);
    }

    private static bool IsValidQuantity(int quantity) =>
        quantity is >= 1 and <= CartItem.MaximumQuantity;

    private static bool IsValidGuestToken(string? token) =>
        token is { Length: GuestTokenLength }
        && token.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static CartMutationResult InvalidQuantity() =>
        CartMutationResult.Failure(
            CartMutationStatus.Invalid,
            $"Escolha entre 1 e {CartItem.MaximumQuantity} unidades.");

    private static CartMutationResult NotFound() =>
        CartMutationResult.Failure(
            CartMutationStatus.NotFound,
            "O item solicitado não foi encontrado.");

    private static CartMutationResult Unavailable(string message) =>
        CartMutationResult.Failure(
            CartMutationStatus.Unavailable,
            message);

    private static string StockMessage(int available) =>
        available <= 0
            ? "Esta variante está temporariamente esgotada."
            : $"Há apenas {available} {(available == 1 ? "unidade disponível" : "unidades disponíveis")} no momento.";

    private sealed record VariantAvailability(
        ProductVariant Variant,
        ProductStatus Status,
        int Available)
    {
        public bool IsSellable =>
            Status == ProductStatus.Published
            && Variant.IsSellable
            && Available > 0;
    }

    private sealed record CartRow(
        long ItemId,
        long VariantId,
        string ProductName,
        string ProductSlug,
        ProductStatus Status,
        int VolumeMl,
        string Sku,
        int Quantity,
        decimal UnitPrice,
        decimal ReferenceUnitPrice,
        string Currency,
        int Available,
        bool IsActive,
        decimal WeightKg,
        decimal HeightCm,
        decimal WidthCm,
        decimal LengthCm,
        string? StorageKey,
        string? AltText,
        int? Width,
        int? Height);
}
