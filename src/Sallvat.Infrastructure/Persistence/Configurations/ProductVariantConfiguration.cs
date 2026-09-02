using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration :
    IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variant", table =>
        {
            table.HasCheckConstraint(
                "ck_product_variant_volume",
                "volume_ml > 0");
            table.HasCheckConstraint(
                "ck_product_variant_price",
                "price >= 0");
            table.HasCheckConstraint(
                "ck_product_variant_physical",
                "weight_kg > 0 AND height_cm > 0 AND width_cm > 0 AND length_cm > 0");
            table.HasCheckConstraint(
                "ck_product_variant_stock",
                "on_hand >= 0 AND reserved >= 0 AND reserved <= on_hand");
            table.HasCheckConstraint(
                "ck_product_variant_currency",
                "currency = 'BRL'");
        });
        builder.HasKey(variant => variant.Id)
            .HasName("pk_product_variant");
        builder.Property(variant => variant.Id).HasColumnName("id");
        builder.Property(variant => variant.ProductId)
            .HasColumnName("product_id");
        builder.Property(variant => variant.Sku)
            .HasColumnName("sku")
            .HasMaxLength(ProductVariant.SkuMaxLength)
            .IsRequired();
        builder.Property(variant => variant.NormalizedSku)
            .HasColumnName("normalized_sku")
            .HasMaxLength(ProductVariant.SkuMaxLength)
            .IsRequired();
        builder.Property(variant => variant.VolumeMl)
            .HasColumnName("volume_ml");
        builder.Property(variant => variant.Price)
            .HasColumnName("price")
            .HasPrecision(18, 2);
        builder.Property(variant => variant.Currency)
            .HasColumnName("currency")
            .HasMaxLength(ProductVariant.CurrencyLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(variant => variant.WeightKg)
            .HasColumnName("weight_kg")
            .HasPrecision(10, 3);
        builder.Property(variant => variant.HeightCm)
            .HasColumnName("height_cm")
            .HasPrecision(10, 2);
        builder.Property(variant => variant.WidthCm)
            .HasColumnName("width_cm")
            .HasPrecision(10, 2);
        builder.Property(variant => variant.LengthCm)
            .HasColumnName("length_cm")
            .HasPrecision(10, 2);
        builder.Property(variant => variant.OnHand)
            .HasColumnName("on_hand");
        builder.Property(variant => variant.Reserved)
            .HasColumnName("reserved");
        builder.Property(variant => variant.IsActive)
            .HasColumnName("is_active");
        builder.Property(variant => variant.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(variant => variant.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(variant => variant.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();
        builder.Ignore(variant => variant.Available);
        builder.Ignore(variant => variant.IsSellable);

        builder.HasIndex(variant => variant.NormalizedSku)
            .IsUnique()
            .HasDatabaseName("ux_product_variant_normalized_sku");
        builder.HasIndex(variant => new
        {
            variant.ProductId,
            variant.IsActive,
        })
            .HasDatabaseName("ix_product_variant_product_active");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_product_variant_product");
    }
}
