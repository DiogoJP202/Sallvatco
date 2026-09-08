using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Catalog;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class CartItemConfiguration :
    IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_item", table =>
        {
            table.HasCheckConstraint(
                "ck_cart_item_quantity",
                $"quantity BETWEEN 1 AND {CartItem.MaximumQuantity}");
            table.HasCheckConstraint(
                "ck_cart_item_reference_price",
                "reference_unit_price >= 0");
            table.HasCheckConstraint(
                "ck_cart_item_currency",
                "currency = 'BRL'");
        });
        builder.HasKey(item => item.Id).HasName("pk_cart_item");
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.CartId).HasColumnName("cart_id");
        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id");
        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.ReferenceUnitPrice)
            .HasColumnName("reference_unit_price")
            .HasPrecision(18, 2);
        builder.Property(item => item.Currency)
            .HasColumnName("currency")
            .HasMaxLength(CartItem.CurrencyLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(item => item.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(item => item.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(item => new
        {
            item.CartId,
            item.ProductVariantId,
        })
            .IsUnique()
            .HasDatabaseName("ux_cart_item_cart_variant");
        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_cart_item_cart");
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cart_item_variant");
    }
}
