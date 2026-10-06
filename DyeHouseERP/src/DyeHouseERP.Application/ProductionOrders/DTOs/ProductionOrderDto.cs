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

    /// <summary>Closed line / open line (الخط المقفول / على المفتوح) - real data, filterable and reportable (spec section 16).</summary>
    public JobOrderType JobOrderType { get; set; }

    public DateTime OrderDate { get; set; }
    public ProductionOrderStatus Status { get; set; }

    /// <summary>
    /// The previous cycle's loss percentage (spec section 19). Populated when a
    /// paused order is resumed, so the resumed cycle starts from the figure the
    /// factory actually achieved last time rather than a guess.
    /// </summary>
    public decimal? PreviousCycleLossPercent { get; set; }

    /// <summary>
    /// Expected output for a resumed cycle, derived from the previous cycle's
    /// output and loss percentage. ADVISORY ONLY: the user still enters the actual
    /// output and the actual loss is recalculated from it.
    /// </summary>
    public decimal? ExpectedOutputKg { get; set; }

    public decimal? ExpectedOutputMeter { get; set; }
    public Guid? ReprocessingOfProductionOrderId { get; set; }

    /// <summary>Set when this Job Order fulfils an approved Formation Request (spec section 31).</summary>
    public Guid? FormationRequestId { get; set; }
    public string? FormationRequestNumber { get; set; }
    public Guid? FormationGroupId { get; set; }
    public int? FormationGroupNumber { get; set; }

    /// <summary>The formation basin this order fulfils, when it was converted from one (spec sections 10-11).</summary>
    public Guid? FormationBasinId { get; set; }
    public int? FormationBasinNumber { get; set; }
    public List<RawAllocationDto> RawAllocations { get; set; } = new();
    public List<StageExecutionDto> StageExecutions { get; set; } = new();
}
