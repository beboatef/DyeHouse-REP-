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
    /// Why an order was completed with no output (spec section 18). A completion that
    /// produced nothing must be explainable, so the reason travels with the order
    /// rather than living only in the audit log. Null for an ordinary completion.
    /// </summary>
    public string? ZeroOutputReason { get; private set; }

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
        JobOrderType jobOrderType = JobOrderType.ClosedLine)
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

        var first = new ProductionOrderStageExecution(Id, formation.Id, formation.Sequence, formation.RequiresDrawingNumber);

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
        ProductionStageDefinition nextStage, decimal? baselineKg, decimal? baselineMeter, string startedBy,
        string? drawingNumber = null)
    {
        var next = new ProductionOrderStageExecution(Id, nextStage.Id, nextStage.Sequence,
            nextStage.RequiresDrawingNumber, drawingNumber);
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

    // =================================================================
    // H1 - the 100% raw-allocation rule (spec section 13)
    // =================================================================

    /// <summary>
    /// Total KG allocated across the allocation rows currently loaded on the
    /// aggregate. KG and Meter are INDEPENDENT units: this figure is never derived
    /// from, and never converted into, <see cref="AllocatedQuantityMeter"/>.
    /// </summary>
    public decimal AllocatedQuantityKg => _rawAllocations.Sum(a => a.QuantityKg ?? 0m);

    /// <summary>Total Meter allocated on the aggregate (see <see cref="AllocatedQuantityKg"/>).</summary>
    public decimal AllocatedQuantityMeter => _rawAllocations.Sum(a => a.QuantityMeter ?? 0m);

    /// <summary>
    /// True when THIS aggregate - allocations loaded with it - may start production.
    /// A plainly loaded order carries none of its allocation rows (they live in their
    /// own table), so handlers must not judge completeness from this property; they
    /// enforce the rule through <c>RawAllocationGuard</c>, which reads the rows back.
    /// </summary>
    public bool HasCompleteRawAllocation =>
        IsRawAllocationComplete(AllocatedQuantityKg, AllocatedQuantityMeter);

    /// <summary>
    /// THE central rule (H1): production may not start - and no production transition
    /// may record stage output, create ready-goods stock or complete the order - until
    /// 100% of the requested quantity has been allocated. KG and Meter are checked
    /// INDEPENDENTLY and are never converted into one another, so a Meter allocation
    /// can never stand in for an outstanding KG requirement (or the reverse).
    /// </summary>
    /// <param name="allocatedQuantityKg">Allocated KG, summed over this order's allocation rows.</param>
    /// <param name="allocatedQuantityMeter">Allocated Meter, summed over this order's allocation rows.</param>
    public bool IsRawAllocationComplete(decimal allocatedQuantityKg, decimal allocatedQuantityMeter)
    {
        // The same tolerance the ready-goods cap uses (EnsureTransfersWithinOutput):
        // a stored 3-decimal quantity must not be refused over rounding dust.
        const decimal tolerance = 0.001m;

        var requestedKg = RequestedQuantityKg ?? 0m;
        var requestedMeter = RequestedQuantityMeter ?? 0m;

        // A unit that was never requested imposes no requirement - and is never
        // satisfied by material allocated in the OTHER unit.
        var kgCovered = requestedKg <= 0m || allocatedQuantityKg + tolerance >= requestedKg;
        var meterCovered = requestedMeter <= 0m || allocatedQuantityMeter + tolerance >= requestedMeter;

        // An order whose requested quantity was never recorded (data predating the
        // current validators) still may not run on nothing: it needs at least one
        // genuinely positive allocation.
        var hasRawMaterial = allocatedQuantityKg > 0m || allocatedQuantityMeter > 0m;

        return kgCovered && meterCovered && hasRawMaterial;
    }

    /// <summary>
    /// Refuses the operation unless 100% of the requested quantity is allocated, with a
    /// message naming the exact shortfall (see <see cref="IsRawAllocationComplete"/>).
    /// </summary>
    public void EnsureRawAllocationComplete(decimal allocatedQuantityKg, decimal allocatedQuantityMeter)
    {
        if (IsRawAllocationComplete(allocatedQuantityKg, allocatedQuantityMeter))
            return;

        throw new DomainException(
            $"Job Order {OrderNumber} is not fully allocated: {allocatedQuantityKg} of {RequestedQuantityKg ?? 0m} KG and " +
            $"{allocatedQuantityMeter} of {RequestedQuantityMeter ?? 0m} Meter are allocated. Production cannot start, no stage " +
            "may record output and no ready-goods stock may be created until 100% of the requested quantity is allocated " +
            "(KG and Meter are independent units and are never converted into one another).");
    }

    /// <summary>
    /// Convenience overload for a caller whose aggregate genuinely carries its allocation
    /// rows; handlers holding a plainly loaded order use the RawAllocationGuard instead.
    /// </summary>
    public void EnsureRawAllocationComplete() =>
        EnsureRawAllocationComplete(AllocatedQuantityKg, AllocatedQuantityMeter);

    public void MarkInProduction()
    {
        // Zero allocations is exactly the Draft state below (the first allocation moves
        // the order to RawAllocated), so this stays the domain-level backstop for the
        // 0% case. The 100% rule - including partial allocation - is enforced by
        // EnsureRawAllocationComplete through RawAllocationGuard on every productive path.
        if (Status == ProductionOrderStatus.Draft)
            throw new DomainException("Cannot start production before raw material has been allocated to this order.");
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        Status = ProductionOrderStatus.InProduction;
    }

    /// <summary>
    /// Edits the order's descriptive and scheduling fields (spec sections 12 + 16). No blanket
    /// immutability: an open order's colour, origin, customer reference, notes, priority and order
    /// date are all ordinary planning data, and the automatic audit interceptor records the old and
    /// new value of each one, with who and when.
    ///
    /// What is deliberately NOT editable here, and why:
    ///   * CustomerId / ItemId - the order's IDENTITY. Every raw allocation, stage execution,
    ///     ready-goods transfer, delivery and invoice already references this order, so changing who
    ///     or what it is would silently re-point all of them at a different party or article.
    ///   * RequestedQuantityKg / RequestedQuantityMeter - the formation stage's IMMUTABLE baseline
    ///     (<see cref="BuildStageRoute"/> snapshots it as the first stage's starting weight) and the
    ///     denominator of the 100%-raw-allocation rule (H1). Changing it here would desync the
    ///     baseline the loss is measured against and silently change whether the order counts as
    ///     fully allocated. A wrong quantity is corrected by cancelling the order (Cancel, which
    ///     reverses the raw material it still holds) and creating a new one with the right figure.
    ///
    /// Neither exclusion rewrites anything historical: they simply are not editable fields. Every
    /// edit made through this method leaves posted ledger rows, recorded stage output and approved
    /// costs untouched.
    /// </summary>
    public void UpdateDetails(
        string? color, string? rawOrigin, string? customerReference, string? notes,
        ProductionPriority priority, DateTime orderDate, string modifiedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        RawOrigin = string.IsNullOrWhiteSpace(rawOrigin) ? null : rawOrigin.Trim();
        CustomerReference = string.IsNullOrWhiteSpace(customerReference) ? null : customerReference.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Priority = priority;
        OrderDate = orderDate;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Changes the line type (closed line / open line) of an order that is not yet completed or
    /// cancelled (spec section 16). The change is gated by the ordinary production.edit permission
    /// and audited with its old and new value, the user and the time - like every other edit here.
    ///
    /// It used to be refused once production had started. That refusal was removed because it was a
    /// hard-coded status restriction with no accounting or inventory reason behind it: the line type
    /// is a CLASSIFICATION of the order (it is only read back for display and filtering - no posted
    /// ledger row, stage output, allocation, delivery or cost is derived from it), so an order that
    /// started as the wrong line type can be corrected without touching any history. Completed and
    /// cancelled orders stay locked, like every other field on them.
    /// </summary>
    public void SetJobOrderType(JobOrderType jobOrderType, string modifiedBy)
    {
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

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
        // A cancelled order is dead and can never come back (spec section 12).
        if (Status == ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        if (_stageExecutions.Any(s => s.Status is StageExecutionStatus.Pending or StageExecutionStatus.InProgress))
            throw new DomainException("Cannot complete a production order while stages are still pending or in progress.");

        Status = ProductionOrderStatus.Completed;
        Lock();
    }

    /// <summary>
    /// Completes the order and records WHY (spec section 18). The reason is mandatory -
    /// a close with no output in particular has to be explainable - and it is kept on the
    /// order itself, not only in the audit log, so it is visible wherever the order is read.
    /// </summary>
    public void CompleteWithReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Completing a production order requires a reason.");
        if (Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", OrderNumber);

        ZeroOutputReason = reason.Trim();
        Complete();
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
