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
    /// Records the receiving inspection result (spec section 11). Rejected
    /// material must NOT become available production stock - callers must
    /// check InspectionStatus before allowing allocation from this message.
    /// </summary>
    public void RecordInspection(InspectionStatus status, string inspector, string? notes, DateTime inspectedAtUtc)
    {
        InspectionStatus = status;
        Inspector = inspector;
        InspectionNotes = notes;
        InspectionDate = inspectedAtUtc;
    }

    /// <summary>Whether material from this message is currently available to allocate against.</summary>
    public bool IsAvailableForAllocation =>
        InspectionStatus is InspectionStatus.Accepted or InspectionStatus.AcceptedWithNotes
        && Status is RawMessageStatus.Open or RawMessageStatus.PartiallyUsed;

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
}
