using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.ProductionOrders.DTOs;

public class RawAllocationDto
{
    public Guid Id { get; set; }
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public string AllocatedBy { get; set; } = string.Empty;
    public DateTime AllocatedAtUtc { get; set; }
}

public class StageExecutionDto
{
    public Guid Id { get; set; }
    public Guid StageDefinitionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public StageExecutionStatus Status { get; set; }
    public decimal? InputKg { get; set; }
    public decimal? InputMeter { get; set; }
    public decimal? OutputKg { get; set; }
    public decimal? OutputMeter { get; set; }
    public decimal? LossKg { get; set; }
    public decimal? LossMeter { get; set; }
    public decimal? SeparatesKg { get; set; }
    public decimal? SeparatesMeter { get; set; }
    public string? Operator { get; set; }
    public string? Notes { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool RequiresApproval { get; set; }
    public bool AllowSkip { get; set; }
}

public class ProductionOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? RequestedQuantityKg { get; set; }
    public decimal? RequestedQuantityMeter { get; set; }
    public string? CustomerReference { get; set; }
    public string? Notes { get; set; }
    public ProductionPriority Priority { get; set; }
    public DateTime OrderDate { get; set; }
    public ProductionOrderStatus Status { get; set; }
    public Guid? ReprocessingOfProductionOrderId { get; set; }
    public List<RawAllocationDto> RawAllocations { get; set; } = new();
    public List<StageExecutionDto> StageExecutions { get; set; } = new();
}
