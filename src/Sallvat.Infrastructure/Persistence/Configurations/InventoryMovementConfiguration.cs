using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Inventory;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class InventoryMovementConfiguration :
    IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movement", table =>
        {
            table.HasCheckConstraint(
                "ck_inventory_movement_quantity",
                "quantity <> 0");
            table.HasCheckConstraint(
                "ck_inventory_movement_balance",
                "resulting_on_hand >= 0 AND resulting_reserved >= 0 AND resulting_reserved <= resulting_on_hand");
        });
        builder.HasKey(movement => movement.Id)
            .HasName("pk_inventory_movement");
        builder.Property(movement => movement.Id).HasColumnName("id");
        builder.Property(movement => movement.ProductVariantId)
            .HasColumnName("product_variant_id");
        builder.Property(movement => movement.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(movement => movement.Quantity)
            .HasColumnName("quantity");
        builder.Property(movement => movement.ResultingOnHand)
            .HasColumnName("resulting_on_hand");
        builder.Property(movement => movement.ResultingReserved)
            .HasColumnName("resulting_reserved");
        builder.Property(movement => movement.ActorUserId)
            .HasColumnName("actor_user_id");
        builder.Property(movement => movement.Reason)
            .HasColumnName("reason")
            .HasMaxLength(InventoryMovement.ReasonMaxLength)
            .IsRequired();
        builder.Property(movement => movement.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(movement => new
        {
            movement.ProductVariantId,
            movement.CreatedAtUtc,
        })
            .HasDatabaseName("ix_inventory_movement_variant_created");
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(movement => movement.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_inventory_movement_variant");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(movement => movement.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_inventory_movement_actor");
    }
}
