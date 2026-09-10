using Sallvat.Domain.Customers;
using Sallvat.Domain.Promotions;

namespace Sallvat.Domain.Orders;

public sealed class Order
{
    public const int OrderNumberMaxLength = 32;
    public const int CurrencyLength = 3;
    public const int ShippingProviderMaxLength = 60;
    public const int ShippingCarrierMaxLength = 120;
    public const int ShippingServiceMaxLength = 120;
    public const int ShippingQuoteIdMaxLength = 160;

    private Order()
    {
    }

    public Order(
        long id,
        string orderNumber,
        Guid checkoutAttemptId,
        Guid sourceCartId,
        long? customerId,
        string buyerName,
        string buyerEmail,
        string buyerPhone,
        decimal itemsSubtotal,
        decimal discountTotal,
        decimal shippingTotal,
        string currency,
        long? couponId,
        string? couponCode,
        string shippingProvider,
        string shippingCarrier,
        string shippingService,
        string shippingQuoteId,
        int shippingMinimumBusinessDays,
        int shippingMaximumBusinessDays,
        DateTimeOffset shippingQuotedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        if (checkoutAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Checkout attempt cannot be empty.",
                nameof(checkoutAttemptId));
        }

        if (sourceCartId == Guid.Empty)
        {
            throw new ArgumentException(
                "Source cart cannot be empty.",
                nameof(sourceCartId));
        }

        if (customerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(customerId));
        }

        Id = id;
        OrderNumber = Required(
            orderNumber,
            OrderNumberMaxLength,
            nameof(orderNumber));
        CheckoutAttemptId = checkoutAttemptId;
        SourceCartId = sourceCartId;
        CustomerId = customerId;
        BuyerName = Required(
            buyerName,
            Customer.NameMaxLength,
            nameof(buyerName));
        BuyerEmail = Required(
            buyerEmail,
            Customer.EmailMaxLength,
            nameof(buyerEmail)).ToLowerInvariant();
        BuyerPhone = Required(
            buyerPhone,
            Customer.PhoneMaxLength,
            nameof(buyerPhone));
        ItemsSubtotal = Money(itemsSubtotal, nameof(itemsSubtotal));
        DiscountTotal = Money(discountTotal, nameof(discountTotal));
        ShippingTotal = Money(shippingTotal, nameof(shippingTotal));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            ShippingTotal,
            nameof(shippingTotal));
        if (DiscountTotal > ItemsSubtotal)
        {
            throw new ArgumentOutOfRangeException(nameof(discountTotal));
        }

        Currency = CurrencyCode(currency);
        if (couponId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(couponId));
        }

        CouponId = couponId;
        CouponCode = Optional(
            couponCode,
            Sallvat.Domain.Promotions.CouponCode.MaxLength,
            nameof(couponCode));
        if ((CouponId.HasValue || CouponCode is not null)
            != DiscountTotal > 0)
        {
            throw new ArgumentException(
                "Coupon snapshot must match the discount total.",
                nameof(couponId));
        }

        ShippingProvider = Required(
            shippingProvider,
            ShippingProviderMaxLength,
            nameof(shippingProvider));
        ShippingCarrier = Required(
            shippingCarrier,
            ShippingCarrierMaxLength,
            nameof(shippingCarrier));
        ShippingService = Required(
            shippingService,
            ShippingServiceMaxLength,
            nameof(shippingService));
        ShippingQuoteId = Required(
            shippingQuoteId,
            ShippingQuoteIdMaxLength,
            nameof(shippingQuoteId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            shippingMinimumBusinessDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            shippingMaximumBusinessDays,
            shippingMinimumBusinessDays);

        ShippingMinimumBusinessDays = shippingMinimumBusinessDays;
        ShippingMaximumBusinessDays = shippingMaximumBusinessDays;
        ShippingQuotedAtUtc = RequireUtc(
            shippingQuotedAtUtc,
            nameof(shippingQuotedAtUtc));
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        ExpiresAtUtc = RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (ShippingQuotedAtUtc > CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(shippingQuotedAtUtc));
        }

        if (ExpiresAtUtc <= CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        GrandTotal = Money(
            ItemsSubtotal - DiscountTotal + ShippingTotal,
            nameof(GrandTotal));
        UpdatedAtUtc = CreatedAtUtc;
        ConcurrencyVersion = Guid.NewGuid();
    }

    public long Id { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public Guid CheckoutAttemptId { get; private set; }

    public Guid SourceCartId { get; private set; }

    public long? CustomerId { get; private set; }

    public Customer? Customer { get; private set; }

    public string BuyerName { get; private set; } = string.Empty;

    public string BuyerEmail { get; private set; } = string.Empty;

    public string BuyerPhone { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; } =
        OrderStatus.PendingPayment;

    public decimal ItemsSubtotal { get; private set; }

    public decimal DiscountTotal { get; private set; }

    public decimal ShippingTotal { get; private set; }

    public decimal GrandTotal { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public long? CouponId { get; private set; }

    public string? CouponCode { get; private set; }

    public string ShippingProvider { get; private set; } = string.Empty;

    public string ShippingCarrier { get; private set; } = string.Empty;

    public string ShippingService { get; private set; } = string.Empty;

    public string ShippingQuoteId { get; private set; } = string.Empty;

    public int ShippingMinimumBusinessDays { get; private set; }

    public int ShippingMaximumBusinessDays { get; private set; }

    public DateTimeOffset ShippingQuotedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyVersion { get; private set; }

    public void AssignGuestCustomer(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (CustomerId.HasValue || Customer is not null)
        {
            throw new InvalidOperationException(
                "Order already belongs to a customer.");
        }

        if (customer.ApplicationUserId.HasValue)
        {
            throw new ArgumentException(
                "Guest customer cannot have an application user.",
                nameof(customer));
        }

        Customer = customer;
    }

    private static decimal Money(decimal value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string CurrencyCode(string value)
    {
        var currency = Required(value, CurrencyLength, nameof(value))
            .ToUpperInvariant();
        if (currency != "BRL")
        {
            throw new ArgumentException(
                "Only BRL is supported in the MVP.",
                nameof(value));
        }

        return currency;
    }

    private static string Required(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string? Optional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, parameterName);

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                parameterName);
        }

        return value;
    }
}
