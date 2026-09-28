using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Supplies.DTOs;

public class SupplyIssueLineDto
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AvailableBalance { get; set; }
    public string? Notes { get; set; }
}

public class SupplyIssueDto
{
    public Guid Id { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string IssuedTo { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public SupplyIssueStatus Status { get; set; }
    public decimal TotalCost { get; set; }
    public bool IsEditable { get; set; }

    public string? PostedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }

    public List<SupplyIssueLineDto> Lines { get; set; } = new();

    /// <summary>Audit timeline: who did what and when, newest first (spec sections 46 + 14).</summary>
    public List<SupplyIssueTimelineDto> Timeline { get; set; } = new();
}

public class SupplyIssueTimelineDto
{
    public string Action { get; set; } = string.Empty;
    public string? User { get; set; }
    public DateTime? AtUtc { get; set; }
    public string? Detail { get; set; }
}
