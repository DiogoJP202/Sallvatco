using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class PaymentRefundRequestConfiguration : IEntityTypeConfiguration<PaymentRefundRequest>
{
    public void Configure(EntityTypeBuilder<PaymentRefundRequest> builder)
    {
        builder.ToTable("payment_refund_request", table =>
        {
            table.HasCheckConstraint("ck_refund_request_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND payment_version <> '00000000-0000-0000-0000-000000000000'::uuid AND order_version <> '00000000-0000-0000-0000-000000000000'::uuid AND concurrency_version <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_refund_request_state", "state = 'Prepared' AND environment = 'Sandbox'");
            table.HasCheckConstraint("ck_refund_request_amount", "amount > 0 AND currency = 'BRL'");
            table.HasCheckConstraint("ck_refund_request_reason", "reason IN ('CustomerRequest', 'FulfillmentUnavailable', 'OperationalCorrection')");
            table.HasCheckConstraint("ck_refund_request_external_ids", "external_order_id ~ '^ORD[A-Za-z0-9_-]+$' AND external_payment_id ~ '^PAY[A-Za-z0-9_-]+$'");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.PaymentId).HasColumnName("payment_id");
        builder.Property(r => r.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Environment).HasColumnName("environment").HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Amount).HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        builder.Property(r => r.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(64);
        builder.Property(r => r.ExternalPaymentId).HasColumnName("external_payment_id").HasMaxLength(64);
        builder.Property(r => r.PaymentVersion).HasColumnName("payment_version");
        builder.Property(r => r.OrderVersion).HasColumnName("order_version");
        builder.Property(r => r.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(r => r.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(r => r.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasOne<Payment>().WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.PaymentId).IsUnique().HasDatabaseName("ux_refund_request_payment");
        builder.HasIndex(r => new { r.State, r.CreatedAtUtc });
    }
}
