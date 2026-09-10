using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Orders;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration :
    IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_item", table =>
        {
            table.HasCheckConstraint(
                "ck_order_item_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_order_item_amounts",
                "unit_price >= 0 AND discount_amount >= 0 AND " +
                "discount_amount <= unit_price * quantity AND " +
                "subtotal = unit_price * quantity - discount_amount");
            table.HasCheckConstraint(
                "ck_order_item_currency",
                "currency = 'BRL'");
        });
        builder.HasKey(item => item.Id).HasName("pk_order_item");
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OrderId).HasColumnName("order_id");
        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id");
        builder.Property(item => item.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(Product.NameMaxLength)
            .IsRequired();
        builder.Property(item => item.VariantName)
            .HasColumnName("variant_name")
            .HasMaxLength(OrderItem.VariantNameMaxLength)
            .IsRequired();
        builder.Property(item => item.Sku)
            .HasColumnName("sku")
            .HasMaxLength(ProductVariant.SkuMaxLength)
            .IsRequired();
        builder.Property(item => item.Quantity)
            .HasColumnName("quantity");
        builder.Property(item => item.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(18, 2);
        builder.Property(item => item.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasPrecision(18, 2);
        builder.Property(item => item.Subtotal)
            .HasColumnName("subtotal")
            .HasPrecision(18, 2);
        builder.Property(item => item.Currency)
            .HasColumnName("currency")
            .HasMaxLength(Order.CurrencyLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(item => item.OrderId)
            .HasDatabaseName("ix_order_item_order_id");
        builder.HasIndex(item => item.ProductVariantId)
            .HasDatabaseName("ix_order_item_variant_id");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_order_item_order");
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_item_variant");
    }
}
