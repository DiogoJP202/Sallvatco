using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Orders;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class OrderAddressConfiguration :
    IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("order_address", table =>
            table.HasCheckConstraint(
                "ck_order_address_country",
                "country_code = 'BR'"));
        builder.HasKey(address => address.OrderId)
            .HasName("pk_order_address");
        builder.Property(address => address.OrderId)
            .HasColumnName("order_id")
            .ValueGeneratedNever();
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

        builder.HasOne<Order>()
            .WithOne()
            .HasForeignKey<OrderAddress>(address => address.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_order_address_order");
    }
}
