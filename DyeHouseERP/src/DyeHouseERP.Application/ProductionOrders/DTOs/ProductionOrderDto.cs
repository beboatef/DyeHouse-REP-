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
    public bool RequiresDrawingNumber { get; set; }
    public string? DrawingNumber { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public StageExecutionStatus Status { get; set; }
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
    // Phase 4: AllowSkip is gone - the legacy skip workflow was deleted, so no stage advertises it.

    /// <summary>The operation's ACTUAL input (spec section 20) - recorded, never inferred from the order's requested quantity.</summary>
    public decimal? InputKg { get; set; }
    public decimal? InputMeter { get; set; }

    /// <summary>Second-grade material produced by this operation - its own quantity, neither output nor waste.</summary>
    public decimal? SecondGradeKg { get; set; }
    public decimal? SecondGradeMeter { get; set; }

    /// <summary>The machine and the execution shift this operation actually ran on (historical references).</summary>
    public Guid? MachineId { get; set; }
    public Guid? ProductionShiftId { get; set; }

    /// <summary>
    /// The operation's WASTE (الهالك), as the floor states it for the last operation before the Ready Goods
    /// warehouse: original raw KG minus the ready weight KG minus the separates KG. The waste is a WEIGHT,
    /// so it exists in KILOGRAMS ONLY - the operation's metre figure is a length and is never converted into
    /// a weight, and there is therefore no "metre waste" figure anywhere in the system. Null (never a
    /// fabricated zero) when the KG input/output was not recorded or when the subtraction would go negative.
    /// See <c>ProductionOrderStageExecution.WasteKg</c>.
    /// </summary>
    public decimal? WasteKg { get; set; }
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
    public string? RawOrigin { get; set; }
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

    /// <summary>
    /// The inventory OUT row (the raw issue) that created this order through the canonical
    /// workflow (spec section 5). Null for legacy/reprocessing orders.
    /// </summary>
    public Guid? SourceIssueTransactionId { get; set; }

    /// <summary>
    /// The customer return this order was raised from, when it was (spec section 33). A reference, not
    /// a relationship the return owns: the order stays independent.
    /// </summary>
    public Guid? CustomerReturnId { get; set; }
    public string? CustomerReturnNumber { get; set; }

    /// <summary>
    /// Who carries this order's cost (spec section 20). Always stated: an ordinary order is worked for
    /// the customer, and an order raised from a customer return carries the account the user chose.
    /// </summary>
    public CostBearer CostBearer { get; set; }

    // =================================================================
    // Original raw accounting across partial deliveries (spec section 31).
    // The original quantity is FIXED for the life of the order and is never overwritten by a
    // delivery; remaining is derived from a single domain rule, per unit, with no conversion.
    // =================================================================

    /// <summary>The order's ORIGINAL raw quantity - fixed, never overwritten.</summary>
    public decimal OriginalRawQuantityKg { get; set; }
    public decimal OriginalRawQuantityMeter { get; set; }

    /// <summary>Original raw quantity settled by this order's non-cancelled deliveries so far.</summary>
    public decimal DeliveredOriginalRawKg { get; set; }
    public decimal DeliveredOriginalRawMeter { get; set; }

    /// <summary>Original PO quantity minus what its deliveries have settled, never negative.</summary>
    public decimal RemainingOriginalRawKg { get; set; }
    public decimal RemainingOriginalRawMeter { get; set; }

    /// <summary>Planning reference: the Formation Request this issue came from, when applicable.</summary>
    public Guid? FormationRequestId { get; set; }

    /// <summary>Planning reference: the formation basin this issue came from, when applicable.</summary>
    public Guid? FormationBasinId { get; set; }

    /// <summary>
    /// True when production has actually started on this order - a stage output, separates or
    /// loss was recorded, a stage was closed/skipped/transferred, or the order is InProduction.
    /// The UI uses this to hide the ordinary unused-raw cancellation, which the backend also
    /// refuses (spec section 9, case C).
    /// </summary>
    public bool HasProductionStarted { get; set; }

    // =================================================================
    // Raw-material allocation: required / allocated / remaining / status
    // (spec section 13). "Required" is the order's own requested quantity
    // (RequestedQuantityKg/Meter). KG and Meter are independent units and are
    // never converted into one another, so each is reported and judged on its own.
    // =================================================================

    /// <summary>Total KG allocated to this order, summed over its allocation rows.</summary>
    public decimal AllocatedQuantityKg { get; set; }

    /// <summary>Total Meter allocated to this order (see <see cref="AllocatedQuantityKg"/>).</summary>
    public decimal AllocatedQuantityMeter { get; set; }

    /// <summary>
    /// Still to allocate, per unit, never negative: the requested quantity minus what is
    /// allocated. A unit the order never requested reports 0 - it imposes no requirement.
    /// </summary>
    public decimal RemainingToAllocateKg { get; set; }

    /// <summary>Meter counterpart of <see cref="RemainingToAllocateKg"/>.</summary>
    public decimal RemainingToAllocateMeter { get; set; }

    /// <summary>
    /// Whether the order is FULLY allocated (100%) - judged by the SAME domain rule the
    /// backend enforces on every production-start/transition path
    /// (ProductionOrder.IsRawAllocationComplete, via RawAllocationGuard), so what the screen
    /// shows and what the server refuses can never disagree. Until this is true, production
    /// cannot start and no stage may record output.
    /// </summary>
    public bool IsRawAllocationComplete { get; set; }

    public List<RawAllocationDto> RawAllocations { get; set; } = new();
    public List<StageExecutionDto> StageExecutions { get; set; } = new();
}
