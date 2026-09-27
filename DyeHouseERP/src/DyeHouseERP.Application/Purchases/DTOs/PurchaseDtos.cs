using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Purchases.DTOs;

/// <summary>
/// Purchases module DTOs (spec section 35). Values that are derived on the
/// aggregate (line value, order total, invoice total) are computed in memory
/// after loading, never projected inside a LINQ-to-Entities query, because they
/// are unmapped domain members.
/// </summary>
public class PurchaseOrderLineDto
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineValue { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal OutstandingQuantity { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? Notes { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public decimal TotalValue { get; set; }
    public decimal ReceivedValue { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<PurchaseOrderLineDto> Lines { get; set; } = new();
}

public class PurchaseReceiptLineDto
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineValue { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseReceiptDto
{
    public Guid Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid? PurchaseOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public string? SupplierDocumentNumber { get; set; }
    public string? Notes { get; set; }
    public decimal TotalValue { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<PurchaseReceiptLineDto> Lines { get; set; } = new();
}

public class SupplierInvoiceLineDto
{
    public Guid Id { get; set; }
    public Guid? MaterialId { get; set; }
    public string? MaterialCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineValue { get; set; }
}

public class SupplierInvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? InternalNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid? PurchaseOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? PurchaseReceiptId { get; set; }
    public string? ReceiptNumber { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EGP";
    public SupplierInvoiceStatus Status { get; set; }
    public string? Notes { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<SupplierInvoiceLineDto> Lines { get; set; } = new();
}

/// <summary>
/// One row of the supplier account statement (spec section 41). Debit = what we
/// owe the supplier (a posted invoice), Credit = what we paid.
/// </summary>
public class SupplierLedgerEntryDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public string SourceDocumentNumber { get; set; } = string.Empty;
    public Guid SourceDocumentId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Running balance including this row - computed in memory, never stored.</summary>
    public decimal RunningBalance { get; set; }
}

/// <summary>Supplier outstanding balance (spec sections 41 and 48) - a live sum over the ledger.</summary>
public class SupplierBalanceDto
{
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public decimal TotalInvoiced { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Outstanding { get; set; }
    public int OpenInvoiceCount { get; set; }
    public decimal OverdueAmount { get; set; }
}
