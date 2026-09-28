using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_event", table =>
        {
            table.HasCheckConstraint("ck_webhook_delivery", "delivery_key ~ '^[0-9A-F]{64}$'");
            table.HasCheckConstraint("ck_webhook_outcome", "outcome IN ('Observed', 'Confirmed', 'RequiresAttention')");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeliveryKey).HasColumnName("delivery_key").HasMaxLength(64);
        builder.Property(e => e.PaymentId).HasColumnName("payment_id");
        builder.Property(e => e.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(64);
        builder.Property(e => e.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ReceivedAtUtc).HasColumnName("received_at_utc").HasColumnType("timestamptz");
        builder.HasIndex(e => e.DeliveryKey).IsUnique().HasDatabaseName("ux_webhook_delivery");
        builder.HasIndex(e => new { e.Outcome, e.ReceivedAtUtc });
        builder.HasOne<Payment>().WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
