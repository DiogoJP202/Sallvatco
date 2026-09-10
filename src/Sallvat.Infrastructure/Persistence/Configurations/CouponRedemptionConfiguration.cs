using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class CouponRedemptionConfiguration :
    IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemption", table =>
        {
            table.HasCheckConstraint(
                "ck_coupon_redemption_identity",
                "customer_id IS NOT NULL OR normalized_email IS NOT NULL");
            table.HasCheckConstraint(
                "ck_coupon_redemption_status",
                "status IN ('Reserved', 'Consumed', 'Released')");
            table.HasCheckConstraint(
                "ck_coupon_redemption_discount",
                "discount_amount > 0");
            table.HasCheckConstraint(
                "ck_coupon_redemption_expiration",
                "expires_at_utc > reserved_at_utc");
            table.HasCheckConstraint(
                "ck_coupon_redemption_order",
                "(status = 'Consumed' AND order_id IS NOT NULL AND consumed_at_utc IS NOT NULL) OR " +
                "(status <> 'Consumed' AND order_id IS NULL AND consumed_at_utc IS NULL)");
        });
        builder.HasKey(redemption => redemption.Id)
            .HasName("pk_coupon_redemption");
        builder.Property(redemption => redemption.Id).HasColumnName("id");
        builder.Property(redemption => redemption.CouponId)
            .HasColumnName("coupon_id");
        builder.Property(redemption => redemption.ReservationKey)
            .HasColumnName("reservation_key");
        builder.Property(redemption => redemption.OrderId)
            .HasColumnName("order_id");
        builder.Property(redemption => redemption.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(redemption => redemption.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(CouponRedemption.NormalizedEmailMaxLength);
        builder.Property(redemption => redemption.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(redemption => redemption.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasPrecision(18, 2);
        builder.Property(redemption => redemption.ReservedAtUtc)
            .HasColumnName("reserved_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(redemption => redemption.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(redemption => redemption.ConsumedAtUtc)
            .HasColumnName("consumed_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(redemption => redemption.ReleasedAtUtc)
            .HasColumnName("released_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(redemption => redemption.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(redemption => redemption.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();

        builder.HasIndex(redemption => redemption.ReservationKey)
            .IsUnique()
            .HasDatabaseName("ux_coupon_redemption_reservation_key");
        builder.HasIndex(redemption => new
        {
            redemption.CouponId,
            redemption.Status,
            redemption.ExpiresAtUtc,
        })
            .HasDatabaseName("ix_coupon_redemption_coupon_status_expiration");
        builder.HasIndex(redemption => new
        {
            redemption.CouponId,
            redemption.CustomerId,
            redemption.Status,
        })
            .HasDatabaseName("ix_coupon_redemption_customer_usage");
        builder.HasIndex(redemption => new
        {
            redemption.CouponId,
            redemption.NormalizedEmail,
            redemption.Status,
        })
            .HasDatabaseName("ix_coupon_redemption_email_usage");
        builder.HasIndex(redemption => redemption.CustomerId)
            .HasDatabaseName("ix_coupon_redemption_customer_id");
        builder.HasIndex(redemption => redemption.OrderId)
            .HasDatabaseName("ix_coupon_redemption_order_id");

        builder.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(redemption => redemption.CouponId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_coupon_redemption_coupon");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(redemption => redemption.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_coupon_redemption_customer");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(redemption => redemption.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_coupon_redemption_order");
    }
}
