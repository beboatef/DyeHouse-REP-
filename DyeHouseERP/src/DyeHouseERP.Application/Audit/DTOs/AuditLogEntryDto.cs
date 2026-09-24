namespace DyeHouseERP.Application.Audit.DTOs;

public class AuditLogEntryDto
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? BeforeDataJson { get; set; }
    public string? AfterDataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? Reason { get; set; }
}
