using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sallvat.Domain.Auditing;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration :
    IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(audit => audit.Id).HasName("pk_audit_log");
        builder.Property(audit => audit.Id).HasColumnName("id");
        builder.Property(audit => audit.ActorUserId)
            .HasColumnName("actor_user_id");
        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasMaxLength(AuditLog.ActionMaxLength)
            .IsRequired();
        builder.Property(audit => audit.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(AuditLog.EntityTypeMaxLength)
            .IsRequired();
        builder.Property(audit => audit.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(AuditLog.EntityIdMaxLength)
            .IsRequired();
        builder.Property(audit => audit.ChangesJson)
            .HasColumnName("changes_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(audit => audit.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(AuditLog.CorrelationIdMaxLength)
            .IsRequired();
        builder.Property(audit => audit.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz");

        builder.HasIndex(audit => new
        {
            audit.EntityType,
            audit.EntityId,
            audit.CreatedAtUtc,
        })
            .HasDatabaseName("ix_audit_log_entity_created");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_audit_log_actor");
    }
}
