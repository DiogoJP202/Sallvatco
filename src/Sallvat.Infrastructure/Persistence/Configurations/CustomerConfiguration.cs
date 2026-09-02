using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Customers;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration :
    IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");
        builder.HasKey(customer => customer.Id)
            .HasName("pk_customer");
        builder.Property(customer => customer.Id)
            .HasColumnName("id");
        builder.Property(customer => customer.ApplicationUserId)
            .HasColumnName("application_user_id");
        builder.Property(customer => customer.Name)
            .HasColumnName("name")
            .HasMaxLength(Customer.NameMaxLength)
            .IsRequired();
        builder.Property(customer => customer.Email)
            .HasColumnName("email")
            .HasMaxLength(Customer.EmailMaxLength)
            .IsRequired();
        builder.Property(customer => customer.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(Customer.EmailMaxLength)
            .IsRequired();
        builder.Property(customer => customer.Phone)
            .HasColumnName("phone")
            .HasMaxLength(Customer.PhoneMaxLength);
        builder.Property(customer => customer.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(customer => customer.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(customer => customer.NormalizedEmail)
            .HasDatabaseName("ix_customer_normalized_email");
        builder.HasIndex(customer => customer.ApplicationUserId)
            .IsUnique()
            .HasDatabaseName("ux_customer_application_user_id");
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Customer>(customer => customer.ApplicationUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_customer_application_user");
    }
}
