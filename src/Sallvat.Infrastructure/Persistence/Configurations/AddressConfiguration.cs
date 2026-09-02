using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Customers;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("address");
        builder.HasKey(address => address.Id)
            .HasName("pk_address");
        builder.Property(address => address.Id).HasColumnName("id");
        builder.Property(address => address.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(address => address.Label)
            .HasColumnName("label")
            .HasMaxLength(Address.LabelMaxLength)
            .IsRequired();
        builder.Property(address => address.RecipientName)
            .HasColumnName("recipient_name")
            .HasMaxLength(Address.RecipientNameMaxLength)
            .IsRequired();
        builder.Property(address => address.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(Address.PostalCodeMaxLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(address => address.Street)
            .HasColumnName("street")
            .HasMaxLength(Address.StreetMaxLength)
            .IsRequired();
        builder.Property(address => address.Number)
            .HasColumnName("number")
            .HasMaxLength(Address.NumberMaxLength)
            .IsRequired();
        builder.Property(address => address.Complement)
            .HasColumnName("complement")
            .HasMaxLength(Address.ComplementMaxLength);
        builder.Property(address => address.District)
            .HasColumnName("district")
            .HasMaxLength(Address.DistrictMaxLength)
            .IsRequired();
        builder.Property(address => address.City)
            .HasColumnName("city")
            .HasMaxLength(Address.CityMaxLength)
            .IsRequired();
        builder.Property(address => address.StateCode)
            .HasColumnName("state_code")
            .HasMaxLength(Address.StateCodeMaxLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(address => address.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(Address.CountryCodeMaxLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(address => address.IsActive)
            .HasColumnName("is_active");
        builder.Property(address => address.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");
        builder.Property(address => address.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(address => new
        {
            address.CustomerId,
            address.IsActive,
        })
            .HasDatabaseName("ix_address_customer_active");
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(address => address.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_address_customer");
    }
}
