using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Promotions;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class CouponConfiguration :
    IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupon", table =>
        {
            table.HasCheckConstraint(
                "ck_coupon_discount_type",
                "discount_type IN ('Percentage', 'FixedAmount')");
            table.HasCheckConstraint("ck_coupon_value", "value > 0");
            table.HasCheckConstraint(
                "ck_coupon_percentage",
                "discount_type <> 'Percentage' OR value <= 100");
            table.HasCheckConstraint(
                "ck_coupon_minimum_subtotal",
                "minimum_subtotal >= 0");
            table.HasCheckConstraint(
                "ck_coupon_total_usage_limit",
                "total_usage_limit IS NULL OR total_usage_limit > 0");
            table.HasCheckConstraint(
                "ck_coupon_usage_limit_per_identity",
                "usage_limit_per_identity IS NULL OR usage_limit_per_identity > 0");
            table.HasCheckConstraint(
                "ck_coupon_claimed_usage_count",
                "claimed_usage_count >= 0 AND " +
                "(total_usage_limit IS NULL OR claimed_usage_count <= total_usage_limit)");
            table.HasCheckConstraint(
                "ck_coupon_window",
                "starts_at_utc IS NULL OR expires_at_utc IS NULL OR " +
                "expires_at_utc > starts_at_utc");
        });
        builder.HasKey(coupon => coupon.Id).HasName("pk_coupon");
        builder.Property(coupon => coupon.Id).HasColumnName("id");
        builder.Property(coupon => coupon.Code)
            .HasColumnName("code")
            .HasMaxLength(CouponCode.MaxLength)
            .IsRequired();
        builder.Property(coupon => coupon.NormalizedCode)
            .HasColumnName("normalized_code")
            .HasMaxLength(CouponCode.MaxLength)
            .IsRequired();
        builder.Property(coupon => coupon.DiscountType)
            .HasColumnName("discount_type")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(coupon => coupon.Value)
            .HasColumnName("value")
            .HasPrecision(18, 2);
        builder.Property(coupon => coupon.MinimumSubtotal)
            .HasColumnName("minimum_subtotal")
            .HasPrecision(18, 2);
        builder.Property(coupon => coupon.StartsAtUtc)
            .HasColumnName("starts_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(coupon => coupon.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(coupon => coupon.TotalUsageLimit)
            .HasColumnName("total_usage_limit");
        builder.Property(coupon => coupon.UsageLimitPerIdentity)
            .HasColumnName("usage_limit_per_identity");
        builder.Property(coupon => coupon.ClaimedUsageCount)
            .HasColumnName("claimed_usage_count");
        builder.Property(coupon => coupon.IsActive)
            .HasColumnName("is_active");
        builder.Property(coupon => coupon.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(coupon => coupon.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(coupon => coupon.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();

        builder.HasIndex(coupon => coupon.NormalizedCode)
            .IsUnique()
            .HasDatabaseName("ux_coupon_normalized_code");
        builder.HasIndex(coupon => new
        {
            coupon.IsActive,
            coupon.StartsAtUtc,
            coupon.ExpiresAtUtc,
        })
            .HasDatabaseName("ix_coupon_active_window");
    }
}
