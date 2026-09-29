using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// The Production Order (أمر التشغيل) - the central operational document of
/// the whole system (spec section 12). Its number is always system-generated.
/// Everything downstream (raw allocation, stage execution, separates,
/// reprocessing, ready goods transfer, delivery, invoicing) references this
/// order's Id, giving full traceability from raw receipt to invoice.
/// </summary>
public class ProductionOrder : AuditableEntity
{
    public string OrderNumber { get; private set; } = string.Empty; // system-generated, e.g. "PRD-2026-000217"
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Color { get; private set; }

    public decimal? RequestedQuantityKg { get; private set; }
    public decimal? RequestedQuantityMeter { get; private set; }

    public string? RawOrigin { get; private set; }           // free-text reference to where the raw came from, if useful
    public string? CustomerReference { get; private set; }   // customer's own request/reference number
    public string? Notes { get; private set; }
    public ProductionPriority Priority { get; private set; } = ProductionPriority.Normal;

    /// <summary>
    /// Closed line / open line (spec section 16: الخط المقفول / على المفتوح).
    /// Stored as real data so it can be filtered, reported, costed and
    /// delivered against - never inferred from free text.
    /// </summary>
    public JobOrderType JobOrderType { get; private set; } = JobOrderType.ClosedLine;

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. Job orders are
    /// touched by many workflows at once (allocations, stage execution,
    /// separate, ready-goods transfer), so a stale in-memory copy can no longer
    /// overwrite a concurrent change.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Set when this Job Order was created from an approved Formation Request
    /// (spec section 31) - the chain Customer -&gt; Raw Material Message -&gt;
    /// Formation Request -&gt; Job Order stays navigable in both directions.
    /// </summary>
    public Guid? FormationRequestId { get; private set; }
    public Guid? FormationGroupId { get; private set; }

    // ---- Job Order costing (spec section 34) ----
    // Three figures, deliberately kept apart so nobody has to guess which one
    // they are looking at:
    //   EstimatedCost - what the order was quoted/planned at (editable while open)
    //   ApprovedCost  - the frozen figure an authorized user signed off on
    //   ActualCost    - NEVER stored here; it is always rolled up live from
    //                   posted MaterialIssue/MaterialPreparation/CostEntry rows
    //                   (see GetProductionOrderCostQuery), so it can never
    //                   drift from the transactions that actually happened.
    public decimal? EstimatedCost { get; private set; }
    public decimal? ApprovedCost { get; private set; }
    public string? CostApprovedBy { get; private set; }
    public DateTime? CostApprovedAtUtc { get; private set; }
    public string? CostingNotes { get; private set; }

    public DateTime OrderDate { get; private set; }
    public ProductionOrderStatus Status { get; private set; } = ProductionOrderStatus.Draft;

    /// <summary>
    /// Set when this order exists to reprocess Separates from another order
    /// (spec section 23). Never null for a reprocessing order; always null
    /// for an original order. The original order's history is never
    /// overwritten - reprocessing always creates a brand new order.
    /// </summary>
    public Guid? ReprocessingOfProductionOrderId { get; private set; }

    private readonly List<ProductionOrderStageExecution> _stageExecutions = new();
    public IReadOnlyCollection<ProductionOrderStageExecution> StageExecutions => _stageExecutions.AsReadOnly();

    private readonly List<RawAllocation> _rawAllocations = new();
    public IReadOnlyCollection<RawAllocation> RawAllocations => _rawAllocations.AsReadOnly();

    private ProductionOrder() { } // EF Core

    public ProductionOrder(
        string orderNumber, Guid customerId, Guid itemId, DateTime orderDate, string createdBy,
        string? color = null, decimal? requestedQuantityKg = null, decimal? requestedQuantityMeter = null,
        string? rawOrigin = null, string? customerReference = null, string? notes = null,
        ProductionPriority priority = ProductionPriority.Normal, Guid? reprocessingOfProductionOrderId = null,
        JobOrderType jobOrderType = JobOrderType.ClosedLine,
        Guid? formationRequestId = null, Guid? formationGroupId = null)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("Order number is required.", nameof(orderNumber));

        OrderNumber = orderNumber;
        CustomerId = customerId;
        ItemId = itemId;
        OrderDate = orderDate;
        Color = color;
        RequestedQuantityKg = requestedQuantityKg;
        RequestedQuantityMeter = requestedQuantityMeter;
        RawOrigin = rawOrigin;
        CustomerReference = customerReference;
        Notes = notes;
        Priority = priority;
        JobOrderType = jobOrderType;
        FormationRequestId = formationRequestId;
        FormationGroupId = formationGroupId;
        ReprocessingOfProductionOrderId = reprocessingOfProductionOrderId;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Generates one Pending stage execution per active stage definition,
    /// in sequence order. Called once, right after creation - the "route"
    /// is snapshotted at that point so later changes to the global stage
    /// configuration don't retroactively alter orders already in flight.
    /// </summary>
    public void BuildStageRoute(IEnumerable<ProductionStageDefinition> activeStagesInSequence)
    {
        if (_stageExecutions.Count > 0)
            throw new DomainException("The stage route has already been built for this production order.");

        foreach (var stage in activeStagesInSequence.OrderBy(s => s.Sequence))
            _stageExecutions.Add(new ProductionOrderStageExecution(Id, stage.Id, stage.Sequence));
    }

    public RawAllocation AllocateRaw(
        Guid rawMessageId, Guid itemId, decimal? quantityKg, decimal? quantityMeter, string allocatedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        var allocation = new RawAllocation(Id, rawMessageId, itemId, quantityKg, quantityMeter, allocatedBy);
        _rawAllocations.Add(allocation);

        if (Status == ProductionOrderStatus.Draft)
            Status = ProductionOrderStatus.RawAllocated;

        return allocation;
    }

    public void MarkInProduction()
    {
        if (Status == ProductionOrderStatus.Draft)
            throw new DomainException("Cannot start production before raw material has been allocated to this order.");
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        Status = ProductionOrderStatus.InProduction;
    }

    /// <summary>Links this order back to the approved Formation Request/group it fulfils (spec section 31).</summary>
    public void LinkFormationRequest(Guid formationRequestId, Guid? formationGroupId, string modifiedBy)
    {
        FormationRequestId = formationRequestId;
        FormationGroupId = formationGroupId;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Changes the line type while the order is still a draft (spec section 16) - never silently after production started.</summary>
    public void SetJobOrderType(JobOrderType jobOrderType, string modifiedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);
        if (Status == ProductionOrderStatus.InProduction)
            throw new DomainException("The line type of an order already in production cannot be changed.");

        JobOrderType = jobOrderType;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records the planned/estimated cost of the order (spec section 34).
    /// Allowed while the order is still open so the estimate can be refined as
    /// the job becomes clearer; refused once the order is completed or
    /// cancelled, because that history is frozen.
    /// </summary>
    public void SetEstimatedCost(decimal? estimatedCost, string? costingNotes, string modifiedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);
        if (estimatedCost is < 0) throw new ArgumentException("Estimated cost cannot be negative.", nameof(estimatedCost));

        EstimatedCost = estimatedCost;
        CostingNotes = string.IsNullOrWhiteSpace(costingNotes) ? CostingNotes : costingNotes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Approves the order's cost (spec section 34). The approved figure is an
    /// explicit decision recorded with who signed it and when - it is never
    /// silently set to the live actual rollup, because an approved cost must
    /// not change afterwards on its own.
    /// </summary>
    public void ApproveCost(decimal approvedCost, string? costingNotes, string approvedBy)
    {
        if (Status == ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);
        if (Status != ProductionOrderStatus.Completed)
            throw new DomainException("The cost of an order can only be approved once the order is completed.");
        if (approvedCost < 0) throw new ArgumentException("Approved cost cannot be negative.", nameof(approvedCost));

        ApprovedCost = approvedCost;
        CostApprovedBy = approvedBy;
        CostApprovedAtUtc = DateTime.UtcNow;
        CostingNotes = string.IsNullOrWhiteSpace(costingNotes) ? CostingNotes : costingNotes.Trim();
        ModifiedBy = approvedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (_stageExecutions.Any(s => s.Status is StageExecutionStatus.Pending or StageExecutionStatus.InProgress))
            throw new DomainException("Cannot complete a production order while stages are still pending or in progress.");

        Status = ProductionOrderStatus.Completed;
        Lock();
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == ProductionOrderStatus.Completed)
            throw new DocumentLockedException("Production Order", OrderNumber);

        Status = ProductionOrderStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled] {reason}" : $"{Notes}\n[Cancelled] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
