namespace DyeHouseERP.Application.PeriodClosing.DTOs;

public class PeriodCloseDto
{
    public Guid Id { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? Notes { get; set; }
    public bool IsReopened { get; set; }
    public string? ReopenedBy { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
