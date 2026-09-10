using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ConfigureTable(builder);
        ConfigureProperties(builder);
        ConfigureIndexes(builder);
        ConfigureRelationships(builder);
    }

    private static void ConfigureTable(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("order", table =>
        {
            table.HasCheckConstraint(
                "ck_order_status",
                "status IN ('PendingPayment', 'Paid', 'Preparing', 'Shipped', 'Delivered', 'Cancelled', 'Refunded', 'RequiresAttention')");
            table.HasCheckConstraint(
                "ck_order_amounts",
                "items_subtotal >= 0 AND discount_total >= 0 AND " +
                "discount_total <= items_subtotal AND shipping_total > 0 AND " +
                "grand_total = items_subtotal - discount_total + shipping_total");
            table.HasCheckConstraint(
                "ck_order_currency",
                "currency = 'BRL'");
            table.HasCheckConstraint(
                "ck_order_coupon",
                "(discount_total = 0 AND coupon_id IS NULL AND coupon_code IS NULL) OR " +
                "(discount_total > 0 AND coupon_id IS NOT NULL AND coupon_code IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_order_shipping_days",
                "shipping_minimum_business_days > 0 AND " +
                "shipping_maximum_business_days >= shipping_minimum_business_days");
            table.HasCheckConstraint(
                "ck_order_expiration",
                "expires_at_utc > created_at_utc AND " +
                "shipping_quoted_at_utc <= created_at_utc");
        });
    }

    private static void ConfigureProperties(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(order => order.Id).HasName("pk_order");
        builder.Property(order => order.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(order => order.OrderNumber)
            .HasColumnName("order_number")
            .HasMaxLength(Order.OrderNumberMaxLength)
            .IsRequired();
        builder.Property(order => order.CheckoutAttemptId)
            .HasColumnName("checkout_attempt_id");
        builder.Property(order => order.SourceCartId)
            .HasColumnName("source_cart_id");
        builder.Property(order => order.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(order => order.BuyerName)
            .HasColumnName("buyer_name")
            .HasMaxLength(Customer.NameMaxLength)
            .IsRequired();
        builder.Property(order => order.BuyerEmail)
            .HasColumnName("buyer_email")
            .HasMaxLength(Customer.EmailMaxLength)
            .IsRequired();
        builder.Property(order => order.BuyerPhone)
            .HasColumnName("buyer_phone")
            .HasMaxLength(Customer.PhoneMaxLength)
            .IsRequired();
        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(order => order.ItemsSubtotal)
            .HasColumnName("items_subtotal")
            .HasPrecision(18, 2);
        builder.Property(order => order.DiscountTotal)
            .HasColumnName("discount_total")
            .HasPrecision(18, 2);
        builder.Property(order => order.ShippingTotal)
            .HasColumnName("shipping_total")
            .HasPrecision(18, 2);
        builder.Property(order => order.GrandTotal)
            .HasColumnName("grand_total")
            .HasPrecision(18, 2);
        builder.Property(order => order.Currency)
            .HasColumnName("currency")
            .HasMaxLength(Order.CurrencyLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(order => order.CouponId)
            .HasColumnName("coupon_id");
        builder.Property(order => order.CouponCode)
            .HasColumnName("coupon_code")
            .HasMaxLength(CouponCode.MaxLength);
        builder.Property(order => order.ShippingProvider)
            .HasColumnName("shipping_provider")
            .HasMaxLength(Order.ShippingProviderMaxLength)
            .IsRequired();
        builder.Property(order => order.ShippingCarrier)
            .HasColumnName("shipping_carrier")
            .HasMaxLength(Order.ShippingCarrierMaxLength)
            .IsRequired();
        builder.Property(order => order.ShippingService)
            .HasColumnName("shipping_service")
            .HasMaxLength(Order.ShippingServiceMaxLength)
            .IsRequired();
        builder.Property(order => order.ShippingQuoteId)
            .HasColumnName("shipping_quote_id")
            .HasMaxLength(Order.ShippingQuoteIdMaxLength)
            .IsRequired();
        builder.Property(order => order.ShippingMinimumBusinessDays)
            .HasColumnName("shipping_minimum_business_days");
        builder.Property(order => order.ShippingMaximumBusinessDays)
            .HasColumnName("shipping_maximum_business_days");
        builder.Property(order => order.ShippingQuotedAtUtc)
            .HasColumnName("shipping_quoted_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(order => order.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(order => order.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(order => order.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(order => order.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(order => order.OrderNumber)
            .IsUnique()
            .HasDatabaseName("ux_order_number");
        builder.HasIndex(order => order.CheckoutAttemptId)
            .IsUnique()
            .HasDatabaseName("ux_order_checkout_attempt");
        builder.HasIndex(order => order.SourceCartId)
            .HasDatabaseName("ix_order_source_cart");
        builder.HasIndex(order => new { order.Status, order.CreatedAtUtc })
            .HasDatabaseName("ix_order_status_created");
        builder.HasIndex(order => order.CustomerId)
            .HasDatabaseName("ix_order_customer_id");
        builder.HasIndex(order => order.CouponId)
            .HasDatabaseName("ix_order_coupon_id");
    }

    private static void ConfigureRelationships(
        EntityTypeBuilder<Order> builder)
    {
        builder.HasOne(order => order.Customer)
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_customer");
        builder.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(order => order.CouponId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_coupon");
    }
}
