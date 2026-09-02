namespace Sallvat.Domain.Inventory;

public sealed class InventoryMovement
{
    public const int ReasonMaxLength = 500;

    private InventoryMovement()
    {
    }

    public InventoryMovement(
        long productVariantId,
        InventoryMovementType type,
        int quantity,
        int resultingOnHand,
        int resultingReserved,
        Guid actorUserId,
        string reason,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productVariantId);
        ArgumentOutOfRangeException.ThrowIfZero(quantity);

        ArgumentOutOfRangeException.ThrowIfNegative(resultingOnHand);
        ArgumentOutOfRangeException.ThrowIfNegative(resultingReserved);
        if (resultingReserved > resultingOnHand)
        {
            throw new ArgumentException(
                "Reserved inventory cannot exceed on-hand inventory.",
                nameof(resultingReserved));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Actor user ID cannot be empty.",
                nameof(actorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > ReasonMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        ProductVariantId = productVariantId;
        Type = type;
        Quantity = quantity;
        ResultingOnHand = resultingOnHand;
        ResultingReserved = resultingReserved;
        ActorUserId = actorUserId;
        Reason = normalizedReason;
        CreatedAtUtc = createdAtUtc.Offset == TimeSpan.Zero
            ? createdAtUtc
            : throw new ArgumentException(
                "Timestamp must use the UTC offset.",
                nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public long ProductVariantId { get; private set; }

    public InventoryMovementType Type { get; private set; }

    public int Quantity { get; private set; }

    public int ResultingOnHand { get; private set; }

    public int ResultingReserved { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
