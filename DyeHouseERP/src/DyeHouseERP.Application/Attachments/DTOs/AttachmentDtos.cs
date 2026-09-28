namespace DyeHouseERP.Application.Attachments.DTOs;

/// <summary>Metadata only - the bytes are never sent in a list response (spec section 47).</summary>
public class AttachmentDto
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Description { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
}

public class AttachmentContentDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// The document types that accept attachments. A fixed list (rather than any
/// string) stops orphan attachments being written against typos or arbitrary
/// table names - the attachment always belongs to a real aggregate.
/// </summary>
public static class AttachmentEntityTypes
{
    public const string Customer = "Customer";
    public const string RawMessage = "RawMessage";
    public const string ProductionOrder = "ProductionOrder";
    public const string FormationRequest = "FormationRequest";
    public const string Delivery = "Delivery";
    public const string Invoice = "Invoice";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string PurchaseReceipt = "PurchaseReceipt";
    public const string SupplierInvoice = "SupplierInvoice";
    public const string SupplierPayment = "SupplierPayment";
    public const string Check = "Check";
    public const string PayrollRun = "PayrollRun";
    public const string Employee = "Employee";
    public const string MaterialSale = "MaterialSale";
    public const string SupplyIssue = "SupplyIssue";
    public const string StockAdjustment = "StockAdjustment";

    public static readonly string[] All =
    {
        Customer, RawMessage, ProductionOrder, FormationRequest, Delivery, Invoice,
        PurchaseOrder, PurchaseReceipt, SupplierInvoice, SupplierPayment, Check,
        PayrollRun, Employee, MaterialSale, SupplyIssue, StockAdjustment
    };

    public static bool IsKnown(string? entityType) =>
        entityType is not null && All.Contains(entityType, StringComparer.Ordinal);
}
