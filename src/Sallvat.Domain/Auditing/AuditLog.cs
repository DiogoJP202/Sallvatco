namespace Sallvat.Domain.Auditing;

public sealed class AuditLog
{
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int CorrelationIdMaxLength = 128;

    private AuditLog()
    {
    }

    public AuditLog(
        Guid actorUserId,
        string action,
        string entityType,
        string entityId,
        string changesJson,
        string correlationId,
        DateTimeOffset createdAtUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Actor user ID cannot be empty.",
                nameof(actorUserId));
        }

        ActorUserId = actorUserId;
        Action = Required(action, ActionMaxLength, nameof(action));
        EntityType = Required(
            entityType,
            EntityTypeMaxLength,
            nameof(entityType));
        EntityId = Required(entityId, EntityIdMaxLength, nameof(entityId));
        ChangesJson = Required(changesJson, int.MaxValue, nameof(changesJson));
        CorrelationId = Required(
            correlationId,
            CorrelationIdMaxLength,
            nameof(correlationId));
        CreatedAtUtc = createdAtUtc.Offset == TimeSpan.Zero
            ? createdAtUtc
            : throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string ChangesJson { get; private set; } = "{}";

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static string Required(
        string value,
        int maxLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }
}
