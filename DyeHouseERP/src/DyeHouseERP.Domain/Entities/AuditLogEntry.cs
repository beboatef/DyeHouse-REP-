using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Append-only record of a state-changing action (spec section 42).
/// Populated automatically by AuditSaveChangesInterceptor for every
/// Added/Modified/Deleted entity tracked by EF Core, plus explicitly for
/// events that aren't entity changes (login, logout, permission changes).
/// </summary>
public class AuditLogEntry : BaseEntity
{
    public DateTime OccurredAtUtc { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty; // Create, Update, Delete, Login, Logout, Approve, Reverse, ...
    public string EntityName { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? BeforeDataJson { get; private set; }
    public string? AfterDataJson { get; private set; }
    public string? IpAddress { get; private set; }
    public string? Reason { get; private set; }

    private AuditLogEntry() { } // EF Core

    public AuditLogEntry(
        DateTime occurredAtUtc, string userName, string action, string entityName,
        string? entityId, string? beforeDataJson, string? afterDataJson, string? ipAddress, string? reason)
    {
        OccurredAtUtc = occurredAtUtc;
        UserName = userName;
        Action = action;
        EntityName = entityName;
        EntityId = entityId;
        BeforeDataJson = beforeDataJson;
        AfterDataJson = afterDataJson;
        IpAddress = ipAddress;
        Reason = reason;
    }
}
