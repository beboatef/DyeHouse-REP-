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

/// <summary>
/// The file kinds an attachment may be (spec section 47 + Phase 1 finding F.8). This is an
/// ALLOWLIST, not a denylist: the bytes are served back from the API origin via
/// <c>File(content, contentType, fileName)</c>, so an HTML, SVG, XML or script file would be
/// rendered by the browser as active content on that origin - a stored-XSS vector. Because a
/// denylist can never enumerate every dangerous type, only known-safe document and image kinds
/// are accepted, and the declared content type must agree with that list.
/// </summary>
public static class AttachmentFileTypes
{
    public static readonly string[] AllowedExtensions =
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".rtf",
        ".odt", ".ods", ".zip"
    };

    public static readonly string[] AllowedContentTypes =
    {
        "application/pdf",
        "image/png", "image/jpeg", "image/webp", "image/gif", "image/bmp",
        "text/plain", "text/csv", "application/rtf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/zip", "application/x-zip-compressed",
        // Some browsers send this for a file they cannot classify. It is only ever accepted
        // alongside an allowed extension, so it cannot smuggle in an active type.
        "application/octet-stream"
    };

    /// <summary>A file is accepted only when BOTH its extension and its declared type are on the allowlist.</summary>
    public static bool IsAllowed(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension) ||
            !AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;

        var normalized = NormalizeContentType(contentType);
        // A missing type is tolerated (the extension already gates the file); a present type must match.
        return normalized.Length == 0 || AllowedContentTypes.Contains(normalized);
    }

    /// <summary>Strips a charset/parameter suffix and lower-cases, so "text/plain; charset=utf-8" matches.</summary>
    public static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return string.Empty;
        var value = contentType.Trim();
        var separator = value.IndexOf(';');
        if (separator >= 0) value = value[..separator];
        return value.Trim().ToLowerInvariant();
    }
}
