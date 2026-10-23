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
    /// The PRIMARY permit this one was issued against, when it is a sub-customer permit (spec section
    /// 31). Null on an ordinary delivery. Stored as a real reference so the traceability chain primary
    /// permit -> sub-customer permit never depends on a code prefix, a note or naming convention.
    ///
    /// Issuing a sub-customer permit is a DOCUMENT action only - it posts no stock. The physical
    /// delivery stays a separate transaction (MarkDelivered), recorded exactly once.
    /// </summary>
    public Guid? PrimaryDeliveryId { get; private set; }

    /// <summary>
    /// The primary customer the linked primary permit was issued to. Kept on the sub-customer permit
    /// because that is the ONLY thing the sub-customer's document is allowed to show about them: the
    /// permit names the primary permit and the primary customer it belongs to, and never carries the
    /// primary customer's own record (charges, ledger, contacts) across.
    /// </summary>
    public Guid? PrimaryCustomerId { get; private set; }

    /// <summary>True when this document is a sub-customer permit (issued against a primary one).</summary>
    public bool IsSubCustomerPermit => PrimaryDeliveryId.HasValue;

    /// <summary>
    /// The client idempotency token of the confirmation that POSTED this delivery's stock (spec section
    /// 31's "deducts exactly once, idempotent against retries"). A double-submitted or retried
    /// confirmation with the same token answers with this document instead of trying to post a second
    /// deduction. Null while the permit is still a draft, and null for rows posted before the token
    /// existed - a historical delivery never has one invented for it.
    /// </summary>
    public Guid? DeliverRequestId { get; private set; }

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

    public Delivery(
        string deliveryNumber, DateTime deliveryDate, Guid customerId, string createdBy, string? notes = null,
        Guid? primaryDeliveryId = null, Guid? primaryCustomerId = null)
    {
        DeliveryNumber = deliveryNumber;
        DeliveryDate = deliveryDate;
        CustomerId = customerId;
        Notes = notes;
        PrimaryDeliveryId = primaryDeliveryId;
        PrimaryCustomerId = primaryCustomerId;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public DeliveryLine AddLine(Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes,
        // The ORIGINAL RAW quantity this line accounts for (spec section 31's delivery statement).
        // Manually entered per delivery, never derived: a partial delivery of finished goods says
        // how much original raw material it settles, and that is what "remaining original raw" is
        // measured against. Optional because a delivery line genuinely may not carry it.
        decimal? originalRawQuantityKg = null, decimal? originalRawQuantityMeter = null)
    {
        if (Status != DeliveryStatus.Draft)
            throw new DocumentLockedException("Delivery", DeliveryNumber);
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A delivery line must specify a KG and/or Meter quantity.");
        if (quantityKg is < 0 || quantityMeter is < 0)
            throw new DomainException("A delivery quantity cannot be negative.");
        if (pieceCount is < 0)
            throw new DomainException("A delivery piece count cannot be negative.");
        if (originalRawQuantityKg is < 0 || originalRawQuantityMeter is < 0)
            throw new DomainException("An original raw quantity cannot be negative.");

        var line = new DeliveryLine(
            Id, productionOrderId, itemId, color, quantityKg, quantityMeter, pieceCount, rawOrigin, notes,
            originalRawQuantityKg, originalRawQuantityMeter);
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
    public void MarkDelivered(Guid? deliverRequestId = null)
    {
        if (Status != DeliveryStatus.Prepared) throw new DomainException($"Delivery is {Status}, expected Prepared.");
        Status = DeliveryStatus.Delivered;
        // The client idempotency token of the confirmation that actually posted the stock (see
        // DeliverRequestId). Stored on the document, never on a display field.
        DeliverRequestId = deliverRequestId;
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

    /// <summary>
    /// The ORIGINAL RAW quantity this delivery line accounts for (spec section 31). Manually entered -
    /// never derived from the finished quantity, because the yield between original raw and ready
    /// goods is exactly what the factory is recording. KG and Meter are independent and are never
    /// converted into one another; null means "this line does not settle original raw".
    /// </summary>
    public decimal? OriginalRawQuantityKg { get; private set; }
    public decimal? OriginalRawQuantityMeter { get; private set; }

    private DeliveryLine() { } // EF Core

    internal DeliveryLine(Guid deliveryId, Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes,
        decimal? originalRawQuantityKg = null, decimal? originalRawQuantityMeter = null)
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
        OriginalRawQuantityKg = originalRawQuantityKg;
        OriginalRawQuantityMeter = originalRawQuantityMeter;
    }

    /// <summary>
    /// Corrects this line's quantities AFTER the delivery was approved
    /// (spec section 30). The caller is responsible for posting the matching
    /// compensating inventory movement - this method only holds the new
    /// figures, and refuses anything that would make the line meaningless.
    /// The pre-edit quantities are captured by the caller into the audit log
    /// before this runs, so the original delivered amount stays recoverable.
    /// </summary>
    public void AdjustQuantitiesAfterApproval(
        decimal? quantityKg, decimal? quantityMeter,
        // The manually-entered original-raw figures travel with the correction: they are per-delivery
        // hand-entered data, not a derived figure, so a corrected delivery must be able to correct
        // them too. Omitted (null) leaves whatever the line already recorded.
        decimal? originalRawQuantityKg = null, decimal? originalRawQuantityMeter = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A delivery line must keep a KG and/or Meter quantity.");
        if (quantityKg is < 0 || quantityMeter is < 0)
            throw new DomainException("A delivery quantity cannot be negative.");
        if (originalRawQuantityKg is < 0 || originalRawQuantityMeter is < 0)
            throw new DomainException("An original raw quantity cannot be negative.");

        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        if (originalRawQuantityKg.HasValue) OriginalRawQuantityKg = originalRawQuantityKg;
        if (originalRawQuantityMeter.HasValue) OriginalRawQuantityMeter = originalRawQuantityMeter;
    }
}
