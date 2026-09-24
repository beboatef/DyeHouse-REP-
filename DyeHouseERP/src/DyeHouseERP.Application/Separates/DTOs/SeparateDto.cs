using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Separates.DTOs;

public class SeparateDto
{
    public Guid Id { get; set; }
    public Guid OriginalProductionOrderId { get; set; }
    public string OriginalOrderNumber { get; set; } = string.Empty;
    public Guid StageExecutionId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public SeparateStatus Status { get; set; }
    public Guid? ReprocessingProductionOrderId { get; set; }
    public string? ReprocessingOrderNumber { get; set; }
}
