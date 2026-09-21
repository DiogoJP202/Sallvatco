using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payment", table =>
        {
            table.HasCheckConstraint("ck_payment_amount", "amount > 0 AND currency = 'BRL'");
            table.HasCheckConstraint("ck_payment_provider", "provider = 'MercadoPago'");
            table.HasCheckConstraint("ck_payment_environment", "environment IN ('Sandbox', 'Production')");
            table.HasCheckConstraint("ck_payment_status",
                "status IN ('Created', 'Pending', 'Approved', 'Rejected', 'Cancelled', 'Expired', 'Refunded', 'RequiresAttention')");
            table.HasCheckConstraint("ck_payment_timestamps",
                "expires_at_utc > created_at_utc AND updated_at_utc >= created_at_utc");
            table.HasCheckConstraint("ck_payment_idempotency_key",
                "idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_payment_reference", "length(btrim(external_reference)) > 0");
            table.HasCheckConstraint("ck_payment_preference", "preference_id IS NULL OR preference_id ~ '^[A-Za-z0-9_-]+$'");
            table.HasCheckConstraint("ck_payment_pending_preference", "status <> 'Pending' OR preference_id IS NOT NULL");
            table.HasCheckConstraint("ck_payment_attention",
                "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND " +
                "attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse')) OR " +
                "(status <> 'RequiresAttention' AND attention_reason IS NULL)");
        });
        builder.HasKey(payment => payment.Id).HasName("pk_payment");
        builder.Property(payment => payment.Id).HasColumnName("id");
        builder.Property(payment => payment.OrderId).HasColumnName("order_id");
        builder.Property(payment => payment.Provider).HasColumnName("provider").HasMaxLength(32);
        builder.Property(payment => payment.Environment).HasColumnName("environment").HasConversion<string>().HasMaxLength(16);
        builder.Property(payment => payment.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(payment => payment.ExternalReference).HasColumnName("external_reference").HasMaxLength(Order.OrderNumberMaxLength);
        builder.Property(payment => payment.PreferenceId).HasColumnName("preference_id").HasMaxLength(Payment.PreferenceIdMaxLength);
        builder.Property(payment => payment.Amount).HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(payment => payment.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(payment => payment.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        builder.Property(payment => payment.AttentionReason).HasColumnName("attention_reason").HasConversion<string>().HasMaxLength(32);
        builder.Property(payment => payment.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(payment => payment.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        builder.Property(payment => payment.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamptz");
        builder.Property(payment => payment.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

        builder.HasIndex(payment => new { payment.Provider, payment.Environment, payment.IdempotencyKey })
            .IsUnique().HasDatabaseName("ux_payment_idempotency");
        builder.HasIndex(payment => new { payment.Provider, payment.Environment, payment.PreferenceId })
            .IsUnique().HasFilter("preference_id IS NOT NULL").HasDatabaseName("ux_payment_preference");
        builder.HasIndex(payment => payment.OrderId)
            .IsUnique().HasFilter("status IN ('Created', 'Pending', 'Approved', 'RequiresAttention')")
            .HasDatabaseName("ux_payment_unresolved_order");
        builder.HasIndex(payment => new { payment.Status, payment.ExpiresAtUtc })
            .HasDatabaseName("ix_payment_status_expiration");
        builder.HasOne<Order>().WithMany().HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payment_order");
    }
}
