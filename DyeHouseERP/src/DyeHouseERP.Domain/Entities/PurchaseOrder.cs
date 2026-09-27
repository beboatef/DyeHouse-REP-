using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Purchase order (spec section 35): what the factory has formally ordered from a
/// supplier, for materials, chemicals, spare parts and operating supplies. The
/// order itself has NO stock or accounting effect - receiving moves the stock and
/// the supplier invoice moves the payable, so nothing is ever double counted.
/// </summary>
public class PurchaseOrder : AuditableEntity
{
    public string OrderNumber { get; private set; } = string.Empty; // system-generated, e.g. "PO-2026-000004"
    public DateTime OrderDate { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public DateTime? ExpectedDeliveryDate { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;

    public string? SubmittedBy { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<PurchaseOrderLine> _lines = new();
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    private PurchaseOrder() { } // EF Core

    public PurchaseOrder(string orderNumber, DateTime orderDate, Guid supplierId, Guid warehouseId,
        string createdBy, DateTime? expectedDeliveryDate = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("Purchase order number is required.", nameof(orderNumber));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier is required.", nameof(supplierId));
        if (warehouseId == Guid.Empty) throw new ArgumentException("Destination warehouse is required.", nameof(warehouseId));

        OrderNumber = orderNumber;
        OrderDate = orderDate;
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        ExpectedDeliveryDate = expectedDeliveryDate;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted;

    /// <summary>Total order value derived from the lines - never a stored column that could drift.</summary>
    public decimal TotalValue => _lines.Sum(l => l.LineValue);

    public void UpdateHeader(DateTime orderDate, Guid warehouseId, DateTime? expectedDeliveryDate, string? notes, string modifiedBy)
    {
        EnsureEditable();
        if (warehouseId == Guid.Empty) throw new DomainException("Destination warehouse is required.");

        OrderDate = orderDate;
        WarehouseId = warehouseId;
        ExpectedDeliveryDate = expectedDeliveryDate;
        Notes = notes;
        Touch(modifiedBy);
    }

    public PurchaseOrderLine AddLine(Guid materialId, decimal quantity, MaterialUnit unit, decimal unitPrice, string? notes, string createdBy)
    {
        EnsureEditable();
        if (quantity <= 0) throw new DomainException("An ordered quantity must be greater than zero.");
        if (unitPrice < 0) throw new DomainException("A unit price cannot be negative.");

        var line = new PurchaseOrderLine(Id, materialId, quantity, unit, unitPrice, notes);
        _lines.Add(line);
        Touch(createdBy);
        return line;
    }

    public void UpdateLine(Guid lineId, decimal quantity, MaterialUnit unit, decimal unitPrice, string? notes, string modifiedBy)
    {
        EnsureEditable();
        var line = FindLine(lineId);
        if (line.ReceivedQuantity > 0)
            throw new DomainException("A line that has already been received from can no longer be edited.");
        if (quantity <= 0) throw new DomainException("An ordered quantity must be greater than zero.");
        if (unitPrice < 0) throw new DomainException("A unit price cannot be negative.");

        line.Update(quantity, unit, unitPrice, notes);
        Touch(modifiedBy);
    }

    public void RemoveLine(Guid lineId, string modifiedBy)
    {
        EnsureEditable();
        var line = FindLine(lineId);
        if (line.ReceivedQuantity > 0)
            throw new DomainException("A line that has already been received from can no longer be removed.");

        _lines.Remove(line);
        Touch(modifiedBy);
    }

    public void Submit(string submittedBy)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException("Only a draft purchase order can be submitted.");
        if (_lines.Count == 0)
            throw new DomainException("A purchase order must contain at least one line before it can be submitted.");

        Status = PurchaseOrderStatus.Submitted;
        SubmittedBy = submittedBy;
        SubmittedAtUtc = DateTime.UtcNow;
        Touch(submittedBy);
    }

    public void Approve(string approvedBy)
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException("Only a submitted purchase order can be approved.");

        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        Touch(approvedBy);
    }

    public void Cancel(string cancelledBy, string reason)
    {
        if (Status == PurchaseOrderStatus.Cancelled) return;
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");

        // Anything already received stays received - the stock is real. Only the
        // outstanding part of the order is cancelled.
        Status = PurchaseOrderStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        Touch(cancelledBy);
    }

    /// <summary>
    /// Records that one line's quantity physically arrived. Accumulates (never
    /// overwrites) and re-derives the order status, so a partial delivery can
    /// never silently look complete (spec sections 35 and 53).
    /// </summary>
    public void RecordReceivedQuantity(Guid lineId, decimal quantity, MaterialUnit unit, string modifiedBy)
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new DomainException("Stock can only be received against an approved purchase order.");

        var line = FindLine(lineId);
        if (line.Unit != unit)
            throw new DomainException("The received unit must match the ordered unit - units are never auto-converted (spec section 6).");
        if (quantity <= 0)
            throw new DomainException("A received quantity must be greater than zero.");

        var remaining = line.Quantity - line.ReceivedQuantity;
        if (quantity > remaining + 0.001m)
            throw new DomainException(
                $"Received quantity ({quantity}) exceeds the outstanding quantity on the order line ({remaining}).");

        line.AddReceivedQuantity(quantity);
        RecalculateReceiptStatus();
        Touch(modifiedBy);
    }

    public void RecalculateReceiptStatus()
    {
        if (Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted
            or PurchaseOrderStatus.Cancelled)
            return;

        var ordered = _lines.Sum(l => l.Quantity);
        var received = _lines.Sum(l => l.ReceivedQuantity);

        Status = (received <= 0, ordered > 0 && received + 0.001m >= ordered) switch
        {
            (true, _) => PurchaseOrderStatus.Approved,
            (_, true) => PurchaseOrderStatus.Received,
            _ => PurchaseOrderStatus.PartiallyReceived
        };
    }

    private PurchaseOrderLine FindLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException($"Purchase order line ({lineId}) does not belong to order {OrderNumber}.");

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DocumentLockedException("Purchase Order", OrderNumber);
        if (Status == PurchaseOrderStatus.Submitted && IsLocked)
            throw new DocumentLockedException("Purchase Order", OrderNumber);
    }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>One ordered material/chemical on a purchase order (spec section 35).</summary>
public class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public MaterialUnit Unit { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string? Notes { get; private set; }

    public decimal LineValue => Quantity * UnitPrice;
    public decimal OutstandingQuantity => Quantity - ReceivedQuantity;

    private PurchaseOrderLine() { } // EF Core

    internal PurchaseOrderLine(Guid purchaseOrderId, Guid materialId, decimal quantity,
        MaterialUnit unit, decimal unitPrice, string? notes)
    {
        PurchaseOrderId = purchaseOrderId;
        MaterialId = materialId;
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        Notes = notes;
    }

    internal void Update(decimal quantity, MaterialUnit unit, decimal unitPrice, string? notes)
    {
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        Notes = notes;
    }

    internal void AddReceivedQuantity(decimal quantity) => ReceivedQuantity += quantity;
}
