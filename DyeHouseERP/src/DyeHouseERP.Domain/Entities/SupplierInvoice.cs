using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Supplier invoice (spec sections 35 and 41). Unlike the customer invoice, a
/// supplier invoice is not produced by this system - it is the supplier's own
/// document, recorded here with its number and due date. POSTING it is the single
/// moment a payable appears on the supplier account, so a purchase order and its
/// goods receipt never create a second, double-counted liability.
/// </summary>
public class SupplierInvoice : AuditableEntity
{
    public string InvoiceNumber { get; private set; } = string.Empty; // the supplier's own number
    public string? InternalNumber { get; private set; } // system reference, when the supplier has none
    public DateTime InvoiceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public Guid? PurchaseReceiptId { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Tax { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public string? Notes { get; private set; }
    public SupplierInvoiceStatus Status { get; private set; } = SupplierInvoiceStatus.Draft;

    public string? PostedBy { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. Posting a
    /// supplier invoice creates the payable leg that supplier payments settle
    /// against, so a post racing a cancel must not both commit.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<SupplierInvoiceLine> _lines = new();
    public IReadOnlyCollection<SupplierInvoiceLine> Lines => _lines.AsReadOnly();

    private SupplierInvoice() { } // EF Core

    public SupplierInvoice(string invoiceNumber, DateTime invoiceDate, DateTime dueDate, Guid supplierId,
        string createdBy, Guid? purchaseOrderId = null, Guid? purchaseReceiptId = null,
        string? internalNumber = null, string? notes = null, string currency = "EGP")
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Supplier invoice number is required.", nameof(invoiceNumber));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier is required.", nameof(supplierId));
        if (dueDate.Date < invoiceDate.Date)
            throw new DomainException("A supplier invoice due date cannot be before its invoice date.");

        InvoiceNumber = invoiceNumber.Trim();
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        SupplierId = supplierId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseReceiptId = purchaseReceiptId;
        InternalNumber = internalNumber;
        Notes = notes;
        Currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency.Trim().ToUpperInvariant();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status == SupplierInvoiceStatus.Draft;

    /// <summary>Derived total - never a stored column that could drift from the lines.</summary>
    public decimal Total => SubTotal - Discount + Tax;

    public void UpdateHeader(DateTime invoiceDate, DateTime dueDate, string? notes, decimal discount, decimal tax, string modifiedBy)
    {
        EnsureEditable();
        if (dueDate.Date < invoiceDate.Date)
            throw new DomainException("A supplier invoice due date cannot be before its invoice date.");
        if (discount < 0 || tax < 0) throw new DomainException("Discount and tax cannot be negative.");

        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        Notes = notes;
        Discount = discount;
        Tax = tax;
        Touch(modifiedBy);
    }

    public SupplierInvoiceLine AddLine(Guid materialId, string description, decimal quantity,
        MaterialUnit unit, decimal unitPrice, string? notes = null)
    {
        EnsureEditable();
        if (quantity <= 0) throw new DomainException("An invoice line quantity must be greater than zero.");
        if (unitPrice < 0) throw new DomainException("An invoice unit price cannot be negative.");

        var line = new SupplierInvoiceLine(Id, materialId, description, quantity, unit, unitPrice, notes);
        _lines.Add(line);
        SubTotal += line.LineValue;
        return line;
    }

    /// <summary>
    /// Posts the invoice: from this point it is a real payable and its lines are
    /// history. The handler writes exactly one Debit SupplierLedgerEntry for the total.
    /// </summary>
    public void Post(string postedBy)
    {
        if (Status != SupplierInvoiceStatus.Draft)
            throw new DomainException("Only a draft supplier invoice can be posted.");
        if (_lines.Count == 0)
            throw new DomainException("A supplier invoice must contain at least one line before it can be posted.");

        Status = SupplierInvoiceStatus.Posted;
        PostedBy = postedBy;
        PostedAtUtc = DateTime.UtcNow;
        SubTotal = _lines.Sum(l => l.LineValue);
        Touch(postedBy);
    }

    /// <summary>A posted invoice is never deleted - cancelling it is what reverses the payable.</summary>
    public void Cancel(string cancelledBy, string reason)
    {
        if (Status == SupplierInvoiceStatus.Cancelled) return;
        if (!string.IsNullOrWhiteSpace(reason)) CancellationReason = reason.Trim();

        Status = SupplierInvoiceStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAtUtc = DateTime.UtcNow;
        Touch(cancelledBy);
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DocumentLockedException("Supplier Invoice", $"{InvoiceNumber}");
    }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>One line on a supplier invoice (spec section 35).</summary>
public class SupplierInvoiceLine : BaseEntity
{
    public Guid SupplierInvoiceId { get; private set; }
    public Guid? MaterialId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public MaterialUnit Unit { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Notes { get; private set; }

    public decimal LineValue => Quantity * UnitPrice;

    private SupplierInvoiceLine() { } // EF Core

    internal SupplierInvoiceLine(Guid supplierInvoiceId, Guid materialId, string description,
        decimal quantity, MaterialUnit unit, decimal unitPrice, string? notes)
    {
        SupplierInvoiceId = supplierInvoiceId;
        MaterialId = materialId == Guid.Empty ? null : materialId;
        Description = string.IsNullOrWhiteSpace(description) ? "-" : description.Trim();
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        Notes = notes;
    }
}

/// <summary>
/// Append-only supplier account ledger (spec section 41). Debit = what we owe the
/// supplier (a posted invoice), Credit = what we have paid (a supplier payment or
/// an endorsed supplier check). Outstanding balance is always
/// SUM(Debit) - SUM(Credit) - a live sum, never a stored running total.
/// </summary>
public class SupplierLedgerEntry : BaseEntity
{
    public Guid SupplierId { get; private set; }
    public DateTime EntryDate { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private SupplierLedgerEntry() { } // EF Core

    public SupplierLedgerEntry(
        Guid supplierId, DateTime entryDate, DocumentType sourceDocumentType, string sourceDocumentNumber,
        Guid sourceDocumentId, decimal debit, decimal credit, string description, string createdBy)
    {
        SupplierId = supplierId;
        EntryDate = entryDate;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        SourceDocumentId = sourceDocumentId;
        Debit = debit;
        Credit = credit;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
