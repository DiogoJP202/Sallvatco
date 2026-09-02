using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class ProductSlugHistoryConfiguration :
    IEntityTypeConfiguration<ProductSlugHistory>
{
    public void Configure(EntityTypeBuilder<ProductSlugHistory> builder)
    {
        builder.ToTable("product_slug_history");
        builder.HasKey(history => history.Id)
            .HasName("pk_product_slug_history");
        builder.Property(history => history.Id).HasColumnName("id");
        builder.Property(history => history.ProductId)
            .HasColumnName("product_id");
        builder.Property(history => history.Slug)
            .HasColumnName("slug")
            .HasMaxLength(CatalogSlug.MaxLength)
            .IsRequired();
        builder.Property(history => history.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(history => history.Slug)
            .IsUnique()
            .HasDatabaseName("ux_product_slug_history_slug");
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(history => history.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_product_slug_history_product");
    }
}
