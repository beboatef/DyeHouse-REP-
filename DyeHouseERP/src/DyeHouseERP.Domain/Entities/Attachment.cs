using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A private attachment on any business document (spec section 47): customer
/// documents, raw receipts, job orders, formation requests, production
/// records, purchase documents, supplier invoices, checks, delivery and
/// financial documents.
///
/// Polymorphic on purpose (EntityType + EntityId) instead of adding an
/// attachment table per module - the alternative would duplicate the same
/// upload/download/permission logic a dozen times. EntityType is the CLR type
/// name of the owning aggregate (e.g. "ProductionOrder"), which is stable and
/// already used by the audit log's entity naming.
///
/// Contents are stored in the database (varbinary) so attachments stay inside
/// the same backup/restore boundary as the records they belong to; there is
/// no external blob store in this deployment. Downloads always go through the
/// API and are permission-checked - nothing is served from a public path.
/// </summary>
public class Attachment : BaseEntity
{
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = Array.Empty<byte>();
    public string? Description { get; private set; }

    public string UploadedBy { get; private set; } = string.Empty;
    public DateTime UploadedAtUtc { get; private set; }

    private Attachment() { } // EF Core

    public Attachment(string entityType, Guid entityId, string fileName, string? contentType,
        byte[] content, string uploadedBy, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (entityId == Guid.Empty) throw new ArgumentException("Entity id is required.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
        if (content.Length == 0) throw new ArgumentException("Attachment content is empty.", nameof(content));

        EntityType = entityType.Trim();
        EntityId = entityId;
        FileName = Path.GetFileName(fileName.Trim());
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        SizeBytes = content.LongLength;
        Content = content;
        UploadedBy = uploadedBy;
        UploadedAtUtc = DateTime.UtcNow;
        Description = description;
    }

    public void SetDescription(string? description) => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
