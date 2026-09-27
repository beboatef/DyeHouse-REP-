using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Raw material receipt / "Message" (spec sections 10-11). This is THE
/// primary traceability reference for customer-owned raw fabric - every
/// downstream consumption (production, transfer, sale, return) points back
/// to one or more messages by Id, and the specific quantity taken from each
/// is always manually chosen by the user (NO FIFO - spec section 4).
/// </summary>
public class RawMessage : AuditableEntity
{
    public string MessageNumber { get; private set; } = string.Empty; // system-generated, e.g. "MSG-2026-000125"
    public DateTime ReceiptDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string ReceivingUser { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public InspectionStatus InspectionStatus { get; private set; } = InspectionStatus.PendingInspection;
    public DateTime? InspectionDate { get; private set; }
    public string? Inspector { get; private set; }
    public string? InspectionNotes { get; private set; }

    public RawMessageStatus Status { get; private set; } = RawMessageStatus.Open;

    private readonly List<RawMessageLine> _lines = new();
    public IReadOnlyCollection<RawMessageLine> Lines => _lines.AsReadOnly();

    private RawMessage() { } // EF Core

    public RawMessage(string messageNumber, DateTime receiptDate, Guid customerId, Guid warehouseId,
        string receivingUser, string createdBy, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(messageNumber))
            throw new ArgumentException("Message number is required.", nameof(messageNumber));

        MessageNumber = messageNumber;
        ReceiptDate = receiptDate;
        CustomerId = customerId;
        WarehouseId = warehouseId;
        ReceivingUser = receivingUser;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public RawMessageLine AddLine(Guid itemId, decimal? quantityKg, decimal? quantityMeter,
        int? pieceCount, string? notes)
    {
        if (IsLocked)
            throw new DocumentLockedException("Raw Receipt Message", MessageNumber);

        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A raw receipt line must record at least a KG quantity or a Meter quantity.");

        if (quantityKg is <= 0 || quantityMeter is <= 0)
            throw new DomainException("Quantities must be greater than zero.");

        var line = new RawMessageLine(Id, itemId, quantityKg, quantityMeter, pieceCount, notes);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Records the receiving inspection result (spec section 9). Inspection is
    /// recorded information only, NEVER an approval step: a receipt posts and is
    /// allocatable without it. Rejections are captured per line through
    /// <see cref="RecordLineRejection"/> and never silently vanish.
    /// </summary>
    public void RecordInspection(InspectionStatus status, string inspector, string? notes, DateTime inspectedAtUtc)
    {
        InspectionStatus = status;
        Inspector = inspector;
        InspectionNotes = notes;
        InspectionDate = inspectedAtUtc;
    }

    /// <summary>
    /// Records (additional) rejected quantity on one receipt line and returns the
    /// delta that was actually recorded, so the caller can post exactly one
    /// correcting ledger row for it (rejected material stays traceable but stops
    /// being allocatable - spec sections 8-9).
    /// </summary>
    public (decimal? Kg, decimal? Meter) RecordLineRejection(Guid lineId, decimal? rejectedKg, decimal? rejectedMeter)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new DomainException("The raw receipt line being rejected does not belong to this message.");

        return line.RecordRejection(rejectedKg, rejectedMeter);
    }

    /// <summary>Whether any line of this message carries a recorded rejection (spec section 9).</summary>
    public bool HasRejections =>
        _lines.Any(l => l.RejectedQuantityKg is > 0 || l.RejectedQuantityMeter is > 0);

    /// <summary>Whether material from this message is currently available to allocate against.</summary>
    public bool IsAvailableForAllocation =>
        Status is RawMessageStatus.Open or RawMessageStatus.PartiallyUsed;

    public void RecalculateStatus(bool hasRemainingBalance, bool hasAnyUsage)
    {
        Status = (hasRemainingBalance, hasAnyUsage) switch
        {
            (true, false) => RawMessageStatus.Open,
            (true, true) => RawMessageStatus.PartiallyUsed,
            (false, _) => RawMessageStatus.Depleted
        };
    }
}

/// <summary>
/// One item line within a raw receipt message. KG and Meter are independent
/// - a line may carry either or both, never a derived conversion between them.
/// Piece/tob count is optional descriptive information only, never an
/// inventory unit (spec section 5).
/// </summary>
public class RawMessageLine : BaseEntity
{
    public Guid RawMessageId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public int? PieceCount { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// Quantity rejected at receiving inspection (spec section 8 "Rejected quantity
    /// if applicable"). Optional - a line with no rejection has both null. Kept as
    /// real data so rejected material remains traceable to the customer's receipt.
    /// </summary>
    public decimal? RejectedQuantityKg { get; private set; }
    public decimal? RejectedQuantityMeter { get; private set; }

    /// <summary>Received minus rejected, per unit - never derived KG&lt;-&gt;Meter (spec section 6).</summary>
    public decimal? AcceptedQuantityKg => QuantityKg.HasValue ? QuantityKg.Value - (RejectedQuantityKg ?? 0m) : null;
    public decimal? AcceptedQuantityMeter => QuantityMeter.HasValue ? QuantityMeter.Value - (RejectedQuantityMeter ?? 0m) : null;

    private RawMessageLine() { } // EF Core

    internal RawMessageLine(Guid rawMessageId, Guid itemId, decimal? quantityKg, decimal? quantityMeter,
        int? pieceCount, string? notes)
    {
        RawMessageId = rawMessageId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        Notes = notes;
    }

    /// <summary>
    /// Accumulates a rejection against this line, rejecting more than was received.
    /// Returns the delta to post to the ledger (null when nothing changed).
    /// </summary>
    internal (decimal? Kg, decimal? Meter) RecordRejection(decimal? rejectedKg, decimal? rejectedMeter)
    {
        var addKg = rejectedKg ?? 0m;
        var addMeter = rejectedMeter ?? 0m;

        if (addKg < 0 || addMeter < 0)
            throw new DomainException("Rejected quantities cannot be negative.");

        if (addKg > 0 && QuantityKg is null)
            throw new DomainException("A rejected KG quantity cannot be recorded on a line that received no KG quantity.");

        if (addMeter > 0 && QuantityMeter is null)
            throw new DomainException("A rejected Meter quantity cannot be recorded on a line that received no Meter quantity.");

        var totalKg = (RejectedQuantityKg ?? 0m) + addKg;
        var totalMeter = (RejectedQuantityMeter ?? 0m) + addMeter;

        if (QuantityKg.HasValue && totalKg > QuantityKg.Value)
            throw new DomainException(
                $"Total rejected KG quantity ({totalKg}) cannot exceed the received quantity ({QuantityKg.Value}).");

        if (QuantityMeter.HasValue && totalMeter > QuantityMeter.Value)
            throw new DomainException(
                $"Total rejected Meter quantity ({totalMeter}) cannot exceed the received quantity ({QuantityMeter.Value}).");

        RejectedQuantityKg = totalKg > 0 ? totalKg : null;
        RejectedQuantityMeter = totalMeter > 0 ? totalMeter : null;

        return (addKg > 0 ? addKg : null, addMeter > 0 ? addMeter : null);
    }
}
