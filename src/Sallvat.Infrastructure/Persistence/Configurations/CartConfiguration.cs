using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Promotions;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("cart", table =>
            table.HasCheckConstraint(
                "ck_cart_owner",
                "(guest_token_hash IS NOT NULL AND customer_id IS NULL) OR " +
                "(guest_token_hash IS NULL AND customer_id IS NOT NULL)"));
        builder.HasKey(cart => cart.Id).HasName("pk_cart");
        builder.Property(cart => cart.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(cart => cart.GuestTokenHash)
            .HasColumnName("guest_token_hash")
            .HasMaxLength(Cart.GuestTokenHashLength)
            .IsFixedLength();
        builder.Property(cart => cart.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(cart => cart.CouponId)
            .HasColumnName("coupon_id");
        builder.Property(cart => cart.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(cart => cart.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(cart => cart.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(cart => cart.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();

        builder.HasIndex(cart => cart.GuestTokenHash)
            .IsUnique()
            .HasFilter("guest_token_hash IS NOT NULL")
            .HasDatabaseName("ux_cart_guest_token_hash");
        builder.HasIndex(cart => cart.CustomerId)
            .IsUnique()
            .HasFilter("customer_id IS NOT NULL")
            .HasDatabaseName("ux_cart_customer_id");
        builder.HasIndex(cart => cart.ExpiresAtUtc)
            .HasDatabaseName("ix_cart_expires_at_utc");
        builder.HasIndex(cart => cart.CouponId)
            .HasDatabaseName("ix_cart_coupon_id");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(cart => cart.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cart_customer");
        builder.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(cart => cart.CouponId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cart_coupon");
    }
}
