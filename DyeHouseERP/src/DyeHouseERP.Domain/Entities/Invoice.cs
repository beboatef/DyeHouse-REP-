using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Financial document for processing services rendered (spec section 32).
/// Never deleted once finalized - cancellation is a status, always auditable.
/// </summary>
public class Invoice : AuditableEntity
{
    public string InvoiceNumber { get; private set; } = string.Empty;
    public DateTime InvoiceDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? DeliveryId { get; private set; }
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public decimal Discount { get; private set; }
    public decimal Tax { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. This matters
    /// most for payment allocation: two receipts resolving the same invoice at
    /// the same time cannot both write the same status/balance derivation.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public decimal SubTotal => _lines.Sum(l => l.Value);
    public decimal Total => SubTotal - Discount + Tax;

    private Invoice() { } // EF Core

    public Invoice(string invoiceNumber, DateTime invoiceDate, Guid customerId, string createdBy,
        Guid? deliveryId = null, decimal discount = 0, decimal tax = 0, string? notes = null)
    {
        InvoiceNumber = invoiceNumber;
        InvoiceDate = invoiceDate;
        CustomerId = customerId;
        DeliveryId = deliveryId;
        Discount = discount;
        Tax = tax;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a billable line. <paramref name="unit"/> is the unit the SERVICE is priced in
    /// (spec section 34's price list is per stage+unit): it may differ from the unit the
    /// production floor recorded, so the caller bills the APPROVED quantity in THAT unit -
    /// never a converted figure (see the billing guard in the create/append handlers).
    /// <paramref name="costEntryId"/> links a cost entry that is explicitly flagged
    /// chargeable to the customer, so exactly one invoice line may ever bill it.
    /// </summary>
    public InvoiceLine AddLine(Guid? productionOrderId, Guid itemId, string? color,
        decimal quantity, decimal processingPrice, UnitOfMeasure unit, string? notes = null,
        Guid? costEntryId = null)
    {
        if (Status != InvoiceStatus.Draft) throw new DocumentLockedException("Invoice", InvoiceNumber);
        if (quantity <= 0) throw new DomainException("Invoice line quantity must be greater than zero.");
        if (processingPrice < 0) throw new DomainException("Processing price cannot be negative.");

        var line = new InvoiceLine(Id, productionOrderId, itemId, color, quantity, processingPrice, unit, notes, costEntryId);
        _lines.Add(line);
        return line;
    }

    // ---------------------------------------------------------------- draft editing
    // A Draft invoice has posted nothing yet (no customer-ledger row, no delivery link) - it is
    // ordinary working data, so its header and its lines are freely editable behind a configurable
    // permission. The moment it is Issued it is financially real, and it becomes read-only here:
    // further changes go through the cancellation/reversal workflow instead. The totals were always
    // derived (SubTotal => sum of line values, Total => SubTotal - Discount + Tax), so editing a line
    // or the header keeps them consistent with no recalculation step to forget.

    /// <summary>Edits the header fields of a Draft invoice. Refused once the invoice has been issued or cancelled.</summary>
    public void UpdateHeader(DateTime invoiceDate, Guid? deliveryId, decimal discount, decimal tax,
        string? notes, string modifiedBy)
    {
        EnsureDraft();
        if (discount < 0) throw new DomainException("Invoice discount cannot be negative.");
        if (tax < 0) throw new DomainException("Invoice tax cannot be negative.");

        InvoiceDate = invoiceDate;
        DeliveryId = deliveryId;
        Discount = discount;
        Tax = tax;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Edits one line of a Draft invoice in place, keeping its Id so any reference to it stays valid.</summary>
    public InvoiceLine UpdateLine(Guid lineId, decimal quantity, decimal processingPrice, UnitOfMeasure unit,
        string? color, string? notes, string modifiedBy)
    {
        EnsureDraft();
        if (quantity <= 0) throw new DomainException("Invoice line quantity must be greater than zero.");
        if (processingPrice < 0) throw new DomainException("Processing price cannot be negative.");

        var line = FindLine(lineId);
        line.Update(quantity, processingPrice, unit, color, notes);
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
        return line;
    }

    /// <summary>Removes a line from a Draft invoice. Refused once the invoice has been issued or cancelled.</summary>
    public void RemoveLine(Guid lineId, string modifiedBy)
    {
        EnsureDraft();
        _lines.Remove(FindLine(lineId));
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    private InvoiceLine FindLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException($"Invoice line ({lineId}) does not belong to invoice {InvoiceNumber}.");

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
            throw new DocumentLockedException("Invoice", InvoiceNumber);
    }

    public void Issue()
    {
        if (Status != InvoiceStatus.Draft) throw new DomainException($"Invoice is {Status}, expected Draft.");
        if (_lines.Count == 0) throw new DomainException("Cannot issue an invoice with no lines.");
        Status = InvoiceStatus.Issued;
        Lock();
    }

    public void ApplyPayment(decimal amountPaidSoFar)
    {
        if (Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            throw new DomainException($"Cannot apply payment to an invoice that is {Status}.");

        Status = amountPaidSoFar >= Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == InvoiceStatus.Paid) throw new DomainException("Cannot cancel a fully paid invoice.");
        Status = InvoiceStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled] {reason}" : $"{Notes}\n[Cancelled] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

public class InvoiceLine : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public Guid? ProductionOrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Color { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ProcessingPrice { get; private set; }

    /// <summary>
    /// The unit this service is billed in, taken from the service's own price
    /// (spec section 34: a price is per stage+unit) - NOT from whatever unit the
    /// production floor happened to record. This is what lets a job be measured in
    /// KG on the floor and billed per Meter: the quantity is the approved Meter
    /// quantity, and the system never converts one unit into the other.
    /// </summary>
    public UnitOfMeasure Unit { get; private set; } = UnitOfMeasure.KG;

    /// <summary>
    /// Set when this line bills a <see cref="CostEntry"/> that was explicitly flagged
    /// chargeable to the customer (reprocessing/separate costs are never auto-added).
    /// A unique, filtered index makes the same cost entry billable by exactly one line.
    /// </summary>
    public Guid? CostEntryId { get; private set; }

    public decimal Value => Quantity * ProcessingPrice;
    public string? Notes { get; private set; }

    private InvoiceLine() { } // EF Core

    internal InvoiceLine(Guid invoiceId, Guid? productionOrderId, Guid itemId, string? color,
        decimal quantity, decimal processingPrice, UnitOfMeasure unit, string? notes, Guid? costEntryId = null)
    {
        InvoiceId = invoiceId;
        ProductionOrderId = productionOrderId;
        ItemId = itemId;
        Color = color;
        Quantity = quantity;
        ProcessingPrice = processingPrice;
        Unit = unit;
        Notes = notes;
        CostEntryId = costEntryId;
    }

    internal void Update(decimal quantity, decimal processingPrice, UnitOfMeasure unit, string? color, string? notes)
    {
        Quantity = quantity;
        ProcessingPrice = processingPrice;
        Unit = unit;
        Color = color;
        Notes = notes;
    }
}
