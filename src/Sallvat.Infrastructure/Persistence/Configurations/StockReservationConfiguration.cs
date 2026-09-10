using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration :
    IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservation", table =>
        {
            table.HasCheckConstraint(
                "ck_stock_reservation_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_stock_reservation_status",
                "status IN ('Reserved', 'Consumed', 'Released')");
            table.HasCheckConstraint(
                "ck_stock_reservation_expiration",
                "expires_at_utc > reserved_at_utc");
            table.HasCheckConstraint(
                "ck_stock_reservation_completion",
                "(status = 'Reserved' AND consumed_at_utc IS NULL AND released_at_utc IS NULL) OR " +
                "(status = 'Consumed' AND consumed_at_utc IS NOT NULL AND released_at_utc IS NULL) OR " +
                "(status = 'Released' AND consumed_at_utc IS NULL AND released_at_utc IS NOT NULL)");
        });
        builder.HasKey(reservation => reservation.Id)
            .HasName("pk_stock_reservation");
        builder.Property(reservation => reservation.Id)
            .HasColumnName("id");
        builder.Property(reservation => reservation.OrderId)
            .HasColumnName("order_id");
        builder.Property(reservation => reservation.ProductVariantId)
            .HasColumnName("product_variant_id");
        builder.Property(reservation => reservation.Quantity)
            .HasColumnName("quantity");
        builder.Property(reservation => reservation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(reservation => reservation.ReservedAtUtc)
            .HasColumnName("reserved_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(reservation => reservation.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(reservation => reservation.ConsumedAtUtc)
            .HasColumnName("consumed_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(reservation => reservation.ReleasedAtUtc)
            .HasColumnName("released_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(reservation => reservation.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(reservation => reservation.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();

        builder.HasIndex(reservation => new
        {
            reservation.OrderId,
            reservation.ProductVariantId,
        })
            .IsUnique()
            .HasDatabaseName("ux_stock_reservation_order_variant");
        builder.HasIndex(reservation => reservation.ProductVariantId)
            .HasDatabaseName("ix_stock_reservation_variant_id");
        builder.HasIndex(reservation => new
        {
            reservation.Status,
            reservation.ExpiresAtUtc,
        })
            .HasDatabaseName("ix_stock_reservation_status_expiration");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(reservation => reservation.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_reservation_order");
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(reservation => reservation.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_reservation_variant");
    }
}
