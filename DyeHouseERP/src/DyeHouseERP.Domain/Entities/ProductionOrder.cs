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

    /// <summary>
    /// Set when this Job Order fulfils ONE basin of a formation group (spec sections 10-11). Always null for
    /// orders that are not derived from a formation request; always set together with FormationGroupId, so the
    /// chain Request -&gt; Group -&gt; Basin -&gt; Job Order -&gt; Production -&gt; Ready Goods stays navigable and a
    /// basin's quantity is never planned on more than one order.
    /// </summary>
    public Guid? FormationBasinId { get; private set; }

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
        Guid? formationRequestId = null, Guid? formationGroupId = null, Guid? formationBasinId = null)
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
        FormationBasinId = formationBasinId;
        ReprocessingOfProductionOrderId = reprocessingOfProductionOrderId;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Starts the order at التشكيل and NOTHING ELSE (spec sections 12-13).
    ///
    /// The route is deliberately NOT pre-built from every active stage: doing so
    /// would force every basin through one fixed sequence, which is exactly what
    /// the factory does not do - different basins take different routes, and the
    /// next stage is chosen by the user at the moment of transfer. Only the
    /// formation stage is created here, already activated with this order's
    /// ACTUAL weight as its baseline; every later stage is created by
    /// <see cref="ActivateNextStage"/> at transfer time.
    /// </summary>
    public void BuildStageRoute(IEnumerable<ProductionStageDefinition> stageDefinitions, string startedBy)
    {
        if (_stageExecutions.Count > 0)
            throw new DomainException("The stage route has already been built for this production order.");

        // Identified ONLY by the explicit flag - never by Sequence order.
        var formation = stageDefinitions.FirstOrDefault(s => s.IsFormationStage)
            ?? throw new DomainException(
                "No production stage is marked as the formation stage (IsFormationStage). Configure one before creating Job Orders.");

        var first = new ProductionOrderStageExecution(Id, formation.Id, formation.Sequence);

        // First-stage baseline is the Job Order's actual weight (spec section 15).
        first.Activate(RequestedQuantityKg, RequestedQuantityMeter, startedBy);
        _stageExecutions.Add(first);
    }

    /// <summary>
    /// Activates the NEXT stage the user chose, with the previous stage's final
    /// output as its baseline (spec section 14). Only this one stage is created -
    /// the route after it stays open until the user picks again.
    /// </summary>
    public ProductionOrderStageExecution ActivateNextStage(
        ProductionStageDefinition nextStage, decimal? baselineKg, decimal? baselineMeter, string startedBy)
    {
        var next = new ProductionOrderStageExecution(Id, nextStage.Id, nextStage.Sequence);
        next.Activate(baselineKg, baselineMeter, startedBy);
        _stageExecutions.Add(next);
        return next;
    }

    /// <summary>The stage currently in progress, if any.</summary>
    public ProductionOrderStageExecution? CurrentStageExecution =>
        _stageExecutions.FirstOrDefault(s => s.Status == StageExecutionStatus.InProgress);

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
    public void LinkFormationRequest(
        Guid formationRequestId, Guid? formationGroupId, Guid? formationBasinId, string modifiedBy)
    {
        FormationRequestId = formationRequestId;
        FormationGroupId = formationGroupId;
        FormationBasinId = formationBasinId;
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

    /// <summary>
    /// Pauses the order (موقوف مؤقتًا, spec section 19).
    ///
    /// Pausing is NOT cancelling: the order keeps every movement and stage it has
    /// accumulated and can be resumed, which re-issues raw material and returns the
    /// job to التشكيل. The caller is responsible for posting the compensating IN
    /// movements that return the released quantity to the customer's raw stock -
    /// this method only records the state change and the reason.
    /// </summary>
    public void Pause(string reason, string modifiedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);
        if (Status == ProductionOrderStatus.Draft)
            throw new DomainException(
                "There is no raw material to release on a draft order - allocate raw material first.");
        if (Status == ProductionOrderStatus.Paused)
            throw new DomainException($"Production order {OrderNumber} is already paused.");

        Status = ProductionOrderStatus.Paused;
        Notes = string.IsNullOrWhiteSpace(Notes)
            ? $"[Paused] {reason}"
            : $"{Notes}\n[Paused] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Resumes a paused order (spec section 19). The caller re-issues the raw
    /// material as a NEW allocation and a NEW movement - the released movements
    /// from the pause are left exactly as they were posted, so the ledger keeps a
    /// complete record of both cycles.
    /// </summary>
    public void Resume(string reason, string modifiedBy)
    {
        if (Status != ProductionOrderStatus.Paused)
            throw new DomainException(
                $"Production order {OrderNumber} is {Status}; only a paused order can be resumed.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Resuming a paused production order requires a reason.");

        Status = ProductionOrderStatus.InProduction;
        Notes = string.IsNullOrWhiteSpace(Notes)
            ? $"[Resumed] {reason}"
            : $"{Notes}\n[Resumed] {reason}";
        ModifiedBy = modifiedBy;
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
