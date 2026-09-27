using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Goods receipt for materials / chemicals / spare parts / operating supplies
/// (spec section 35). This is the document that actually moves factory-owned
/// stock: the handler posts one IN MaterialTransaction per line, and when the
/// receipt is against a purchase order it also advances that order's received
/// quantities. There is no manual "received" edit on the order itself.
/// </summary>
public class PurchaseReceipt : AuditableEntity
{
    public string ReceiptNumber { get; private set; } = string.Empty; // system-generated, e.g. "GRN-2026-000012"
    public DateTime ReceiptDate { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public string ReceivedBy { get; private set; } = string.Empty;
    public string? SupplierDocumentNumber { get; private set; }
    public string? Notes { get; private set; }

    private readonly List<PurchaseReceiptLine> _lines = new();
    public IReadOnlyCollection<PurchaseReceiptLine> Lines => _lines.AsReadOnly();

    private PurchaseReceipt() { } // EF Core

    public PurchaseReceipt(string receiptNumber, DateTime receiptDate, Guid supplierId, Guid warehouseId,
        string receivedBy, string createdBy, Guid? purchaseOrderId = null,
        string? supplierDocumentNumber = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
            throw new ArgumentException("Goods receipt number is required.", nameof(receiptNumber));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier is required.", nameof(supplierId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse is required.", nameof(warehouseId));

        ReceiptNumber = receiptNumber;
        ReceiptDate = receiptDate;
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        PurchaseOrderId = purchaseOrderId;
        ReceivedBy = receivedBy;
        SupplierDocumentNumber = supplierDocumentNumber;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Received value from the lines - used for the audit trail, never stored separately.</summary>
    public decimal TotalValue => _lines.Sum(l => l.LineValue);

    public PurchaseReceiptLine AddLine(Guid materialId, decimal quantity, MaterialUnit unit,
        decimal unitCost, Guid? purchaseOrderLineId = null, string? notes = null)
    {
        if (quantity <= 0) throw new DomainException("A received quantity must be greater than zero.");
        if (unitCost < 0) throw new DomainException("A unit cost cannot be negative.");

        var line = new PurchaseReceiptLine(Id, materialId, quantity, unit, unitCost, purchaseOrderLineId, notes);
        _lines.Add(line);
        return line;
    }

    public PurchaseReceiptLine? FindLineByPurchaseOrderLine(Guid purchaseOrderLineId) =>
        _lines.FirstOrDefault(l => l.PurchaseOrderLineId == purchaseOrderLineId);
}

/// <summary>One received material line. Unit cost is captured here so material stock keeps a real landed cost (spec sections 24 and 35).</summary>
public class PurchaseReceiptLine : BaseEntity
{
    public Guid PurchaseReceiptId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public MaterialUnit Unit { get; private set; }
    public decimal UnitCost { get; private set; }
    public Guid? PurchaseOrderLineId { get; private set; }
    public string? Notes { get; private set; }

    public decimal LineValue => Quantity * UnitCost;

    private PurchaseReceiptLine() { } // EF Core

    internal PurchaseReceiptLine(Guid purchaseReceiptId, Guid materialId, decimal quantity,
        MaterialUnit unit, decimal unitCost, Guid? purchaseOrderLineId, string? notes)
    {
        PurchaseReceiptId = purchaseReceiptId;
        MaterialId = materialId;
        Quantity = quantity;
        Unit = unit;
        UnitCost = unitCost;
        PurchaseOrderLineId = purchaseOrderLineId;
        Notes = notes;
    }
}
