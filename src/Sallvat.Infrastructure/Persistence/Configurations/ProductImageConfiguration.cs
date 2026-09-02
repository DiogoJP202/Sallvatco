using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class ProductImageConfiguration :
    IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_image", table =>
        {
            table.HasCheckConstraint(
                "ck_product_image_dimensions",
                "width > 0 AND height > 0");
            table.HasCheckConstraint(
                "ck_product_image_position",
                "position >= 0");
        });
        builder.HasKey(image => image.Id).HasName("pk_product_image");
        builder.Property(image => image.Id).HasColumnName("id");
        builder.Property(image => image.ProductId)
            .HasColumnName("product_id");
        builder.Property(image => image.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(ProductImage.StorageKeyMaxLength)
            .IsRequired();
        builder.Property(image => image.AltText)
            .HasColumnName("alt_text")
            .HasMaxLength(ProductImage.AltTextMaxLength)
            .IsRequired();
        builder.Property(image => image.Width).HasColumnName("width");
        builder.Property(image => image.Height).HasColumnName("height");
        builder.Property(image => image.Position).HasColumnName("position");
        builder.Property(image => image.IsCover).HasColumnName("is_cover");
        builder.Property(image => image.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(image => image.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_product_image_storage_key");
        builder.HasIndex(image => image.ProductId)
            .IsUnique()
            .HasFilter("is_cover")
            .HasDatabaseName("ux_product_image_cover");
        builder.HasIndex(image => new { image.ProductId, image.Position })
            .HasDatabaseName("ix_product_image_product_position");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_product_image_product");
    }
}
