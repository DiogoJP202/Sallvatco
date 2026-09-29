using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Payments;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class PaymentRecoveryExecutionConfiguration : IEntityTypeConfiguration<PaymentRecoveryExecution>
{
    public void Configure(EntityTypeBuilder<PaymentRecoveryExecution> builder)
    {
        builder.ToTable("payment_recovery_execution", table =>
        {
            table.HasCheckConstraint("ck_recovery_execution_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_recovery_execution_window", "expires_at_utc = started_at_utc + interval '2 minutes'");
            table.HasCheckConstraint("ck_recovery_execution_state", "(state = 'Running' AND finished_at_utc IS NULL) OR (state = 'Completed' AND finished_at_utc IS NOT NULL AND finished_at_utc >= started_at_utc AND finished_at_utc < expires_at_utc) OR (state = 'Interrupted' AND finished_at_utc IS NOT NULL AND finished_at_utc >= expires_at_utc)");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.PaymentId).HasColumnName("payment_id");
        builder.Property(e => e.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamptz");
        builder.Property(e => e.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("timestamptz");
        builder.Property(e => e.FinishedAtUtc).HasColumnName("finished_at_utc").HasColumnType("timestamptz");
        builder.Property(e => e.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasOne<Payment>().WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.PaymentId).IsUnique().HasFilter("state = 'Running'").HasDatabaseName("ux_recovery_execution_running");
        builder.HasIndex(e => new { e.PaymentId, e.StartedAtUtc });
    }
}
