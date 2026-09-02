using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration :
    IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product", table =>
            table.HasCheckConstraint(
                "ck_product_status",
                "status IN ('Draft', 'Published', 'Archived')"));
        builder.HasKey(product => product.Id).HasName("pk_product");
        builder.Property(product => product.Id).HasColumnName("id");
        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(Product.NameMaxLength)
            .IsRequired();
        builder.Property(product => product.Slug)
            .HasColumnName("slug")
            .HasMaxLength(CatalogSlug.MaxLength)
            .IsRequired();
        builder.Property(product => product.ShortDescription)
            .HasColumnName("short_description")
            .HasMaxLength(Product.ShortDescriptionMaxLength)
            .IsRequired();
        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();
        ConfigureAttribute(
            builder.Property(product => product.OlfactoryFamily),
            "olfactory_family",
            Product.ClassificationMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.TopNotes),
            "top_notes",
            Product.AttributeMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.HeartNotes),
            "heart_notes",
            Product.AttributeMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.BaseNotes),
            "base_notes",
            Product.AttributeMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Concentration),
            "concentration",
            Product.ClassificationMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Projection),
            "projection",
            Product.ClassificationMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Longevity),
            "longevity",
            Product.ClassificationMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Occasions),
            "occasions",
            Product.AttributeMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Season),
            "season",
            Product.ClassificationMaxLength);
        ConfigureAttribute(
            builder.Property(product => product.Period),
            "period",
            Product.ClassificationMaxLength);
        builder.Property(product => product.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(product => product.IsFeatured)
            .HasColumnName("is_featured");
        builder.Property(product => product.PublishedAtUtc)
            .HasColumnName("published_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(product => product.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(product => product.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(product => product.ConcurrencyVersion)
            .HasColumnName("concurrency_version")
            .IsConcurrencyToken();

        builder.HasIndex(product => product.Slug)
            .IsUnique()
            .HasDatabaseName("ux_product_slug");
        builder.HasIndex(product => new
        {
            product.Status,
            product.IsFeatured,
            product.PublishedAtUtc,
        })
            .HasDatabaseName("ix_product_publication");
    }

    private static void ConfigureAttribute(
        PropertyBuilder<string> property,
        string columnName,
        int maxLength) =>
        property
            .HasColumnName(columnName)
            .HasMaxLength(maxLength)
            .IsRequired();
}
