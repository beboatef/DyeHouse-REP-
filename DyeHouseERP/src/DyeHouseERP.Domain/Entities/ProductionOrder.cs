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
        ProductionPriority priority = ProductionPriority.Normal, Guid? reprocessingOfProductionOrderId = null)
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
