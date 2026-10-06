using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Delivery document (spec section 31) - may cover several Production
/// Orders for the same customer. Has no financial value by itself; an
/// Invoice is a separate document that may reference a Delivery.
/// </summary>
public class Delivery : AuditableEntity
{
    public string DeliveryNumber { get; private set; } = string.Empty;
    public DateTime DeliveryDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Draft;
    public string? Notes { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. Two operators
    /// acting on the same delivery (e.g. delivering and cancelling at the same
    /// time) can no longer both commit: the second UPDATE matches zero rows and
    /// EF raises DbUpdateConcurrencyException instead of silently overwriting
    /// the first one's state.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<DeliveryLine> _lines = new();
    public IReadOnlyCollection<DeliveryLine> Lines => _lines.AsReadOnly();

    private Delivery() { } // EF Core

    public Delivery(string deliveryNumber, DateTime deliveryDate, Guid customerId, string createdBy, string? notes = null)
    {
        DeliveryNumber = deliveryNumber;
        DeliveryDate = deliveryDate;
        CustomerId = customerId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public DeliveryLine AddLine(Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes)
    {
        if (Status != DeliveryStatus.Draft)
            throw new DocumentLockedException("Delivery", DeliveryNumber);
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A delivery line must specify a KG and/or Meter quantity.");

        var line = new DeliveryLine(Id, productionOrderId, itemId, color, quantityKg, quantityMeter, pieceCount, rawOrigin, notes);
        _lines.Add(line);
        return line;
    }

    public void MarkPrepared()
    {
        if (Status != DeliveryStatus.Draft) throw new DomainException($"Delivery is {Status}, expected Draft.");
        if (_lines.Count == 0) throw new DomainException("Cannot prepare a delivery with no lines.");
        Status = DeliveryStatus.Prepared;
    }

    /// <summary>
    /// Marks Delivered. Ledger balance validation and posting the OUT
    /// transactions happen in the application handler (needs ledger
    /// access) - this just guards the state transition and then locks the
    /// document from further normal editing (spec: "lock the document from
    /// normal editing" after Delivered).
    /// </summary>
    public void MarkDelivered()
    {
        if (Status != DeliveryStatus.Prepared) throw new DomainException($"Delivery is {Status}, expected Prepared.");
        Status = DeliveryStatus.Delivered;
        Lock();
    }

    /// <summary>
    /// Records that an ALREADY APPROVED (Delivered) delivery was corrected
    /// (spec section 30). This never changes a quantity itself and never touches
    /// the ledger - the handler does that with compensating movements. It only
    /// marks that the document was edited after approval and why, so the reason
    /// stays visible on the document itself and in the audit log.
    ///
    /// The ordinary "locked after Delivered" rule still holds for adding or
    /// removing LINES: an approved delivery can be corrected, but not restructured.
    /// </summary>
    public void EditAfterApproval(string reason, string modifiedBy)
    {
        if (Status != DeliveryStatus.Delivered)
            throw new DomainException($"Only a delivered delivery can be edited after approval; this one is {Status}.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An after-approval edit requires a reason.");

        Notes = string.IsNullOrWhiteSpace(Notes)
            ? $"[Edited after approval] {reason}"
            : $"{Notes}\n[Edited after approval] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == DeliveryStatus.Cancelled) throw new DomainException("Delivery is already cancelled.");
        Status = DeliveryStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled] {reason}" : $"{Notes}\n[Cancelled] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

public class DeliveryLine : BaseEntity
{
    public Guid DeliveryId { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Color { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public int? PieceCount { get; private set; }
    public string? RawOrigin { get; private set; }
    public string? Notes { get; private set; }

    private DeliveryLine() { } // EF Core

    internal DeliveryLine(Guid deliveryId, Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes)
    {
        DeliveryId = deliveryId;
        ProductionOrderId = productionOrderId;
        ItemId = itemId;
        Color = color;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        RawOrigin = rawOrigin;
        Notes = notes;
    }

    /// <summary>
    /// Corrects this line's quantities AFTER the delivery was approved
    /// (spec section 30). The caller is responsible for posting the matching
    /// compensating inventory movement - this method only holds the new
    /// figures, and refuses anything that would make the line meaningless.
    /// The pre-edit quantities are captured by the caller into the audit log
    /// before this runs, so the original delivered amount stays recoverable.
    /// </summary>
    public void AdjustQuantitiesAfterApproval(decimal? quantityKg, decimal? quantityMeter)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A delivery line must keep a KG and/or Meter quantity.");
        if (quantityKg is < 0 || quantityMeter is < 0)
            throw new DomainException("A delivery quantity cannot be negative.");

        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
    }
}
