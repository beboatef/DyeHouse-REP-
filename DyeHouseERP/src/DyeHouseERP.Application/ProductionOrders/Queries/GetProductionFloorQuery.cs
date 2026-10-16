using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Queries;

/// <summary>
/// One line on the factory floor screen: a live Job Order and the stage it is
/// physically sitting in right now, with the figures the supervisor actually
/// needs to judge it - how long it has been there, what went in, what came out
/// and what was lost.
/// </summary>
public class ProductionFloorRowDto
{
    public Guid ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? RequestedQuantityKg { get; set; }
    public decimal? RequestedQuantityMeter { get; set; }

    /// <summary>The stage definition the order is in, or null when raw material is allocated but no stage has started.</summary>
    public Guid? StageDefinitionId { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public StageExecutionStatus CurrentStageStatus { get; set; }
    public Guid? StageExecutionId { get; set; }
    public int? StageSequence { get; set; }

    public ProductionOrderStatus OrderStatus { get; set; }
    public ProductionPriority Priority { get; set; }

    public DateTime? StageStartedAtUtc { get; set; }
    public double? MinutesInStage { get; set; }

    /// <summary>Quantity that entered the current stage (the previous stage's output, or the order's own weight).</summary>
    public decimal? BaselineKg { get; set; }
    public decimal? BaselineMeter { get; set; }
    public decimal? InputKg { get; set; }
    public decimal? InputMeter { get; set; }
    public decimal? OutputKg { get; set; }
    public decimal? OutputMeter { get; set; }
    public decimal? LossKg { get; set; }
    public decimal? LossMeter { get; set; }
    public decimal? LossPercentKg { get; set; }
    public decimal? LossPercentMeter { get; set; }

    public string? Operator { get; set; }

    /// <summary>
    /// True when raw material is allocated but the order has not entered its first stage yet -
    /// the "waiting to start" pile the floor has to chase. It is shown, never hidden.
    /// </summary>
    public bool AwaitingStart { get; set; }

    public string? CustomerReference { get; set; }
    public string? Notes { get; set; }

    // ---- delivered slice (spec section 37) ----

    /// <summary>
    /// True when this job order has actually been DELIVERED to the customer (a delivered permit
    /// exists for it). A delivered job is not work waiting on the floor, so the ordinary view hides
    /// it - it is shown only when the user asks for the delivered orders.
    /// </summary>
    public bool IsDelivered { get; set; }

    /// <summary>The permits the order was delivered on, so the row can be followed into the delivery.</summary>
    public string? DeliveryNumbers { get; set; }
    public DateTime? LastDeliveryDate { get; set; }

    /// <summary>What the deliveries actually handed over, per unit - never merged into one figure.</summary>
    public decimal? DeliveredQuantityKg { get; set; }
    public decimal? DeliveredQuantityMeter { get; set; }
}

/// <summary>
/// Which SLICE of the production cycle the floor screen is showing.
///
/// The default is <see cref="Open"/>: the jobs physically on the floor. A job that has been DELIVERED
/// to the customer is finished work - it stays out of that view (otherwise the supervisor keeps
/// chasing jobs that already left the building) and is shown only when this slice asks for it.
/// </summary>
public enum ProductionFloorView
{
    /// <summary>Still on the floor: not delivered, and not closed. The ordinary view.</summary>
    Open = 1,

    /// <summary>Jobs that have been delivered to the customer (including their final stage).</summary>
    Delivered = 2,

    /// <summary>Closed jobs that were never delivered: completed, or cancelled.</summary>
    Closed = 3,

    /// <summary>Everything, whatever its state.</summary>
    All = 4
}

/// <summary>
/// The factory floor feed (spec section 37): one row per Job Order in the requested slice.
///
/// "On the floor" means every order still physically there - running, waiting for its first stage, or
/// paused. A paused job is not gone: the material is still allocated to it and the supervisor has to
/// see it, so it is listed and clearly marked rather than silently dropped. A DELIVERED job is not on
/// the floor any more, so it belongs to the delivered slice and not to the ordinary one.
///
/// The stage filter reads the order's REAL stage execution (its identity, not the stage's name), so an
/// order can never appear under a stage it has not reached, and stage names are resolved from the
/// configurable stage definitions - nothing about the route is hard-coded here.
/// </summary>
public record GetProductionFloorQuery(
    Guid? StageDefinitionId = null, ProductionOrderStatus? Status = null, Guid? CustomerId = null,
    Guid? ItemId = null, ProductionPriority? Priority = null, string? Search = null,
    ProductionFloorView View = ProductionFloorView.Open) : IRequest<List<ProductionFloorRowDto>>;

public class GetProductionFloorQueryHandler : IRequestHandler<GetProductionFloorQuery, List<ProductionFloorRowDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionFloorQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionFloorRowDto>> Handle(GetProductionFloorQuery request, CancellationToken cancellationToken)
    {
        // ---- the delivered slice, from the delivery documents themselves ----------------------
        // "Delivered" is a fact about the ORDERS' PERMITS, not a status somebody has to remember to
        // set: an order counts as delivered exactly when a permit for one of its lines has been
        // delivered. Cancelling that permit puts the job straight back into the open slice, which is
        // the behaviour a floor supervisor expects.
        var deliveredDocs = await _db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Delivered)
            .Select(d => new { d.Id, d.DeliveryNumber, d.DeliveryDate })
            .ToListAsync(cancellationToken);

        var deliveredDocIds = deliveredDocs.Select(d => d.Id).ToList();
        var deliveredLines = deliveredDocIds.Count == 0
            ? new List<DeliveryLine>()
            : await _db.DeliveryLines.AsNoTracking()
                .Where(l => deliveredDocIds.Contains(l.DeliveryId)).ToListAsync(cancellationToken);

        var deliveriesByOrder = deliveredLines
            .GroupBy(l => l.ProductionOrderId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.DeliveryId).Distinct().ToList());
        var deliveredOrderIds = deliveriesByOrder.Keys.ToHashSet();

        var ordersQuery = _db.ProductionOrders.AsNoTracking().AsQueryable();

        // An explicit status filter is the user's own choice and wins: asking for "Completed" must show
        // completed jobs even though the ordinary floor view would never list them.
        if (request.Status.HasValue)
            ordersQuery = ordersQuery.Where(o => o.Status == request.Status.Value);
        else
            ordersQuery = request.View switch
            {
                ProductionFloorView.Open => ordersQuery.Where(o =>
                    o.Status == ProductionOrderStatus.InProduction
                    || o.Status == ProductionOrderStatus.RawAllocated
                    || o.Status == ProductionOrderStatus.Paused),
                ProductionFloorView.Closed => ordersQuery.Where(o =>
                    o.Status == ProductionOrderStatus.Completed
                    || o.Status == ProductionOrderStatus.Cancelled),
                // The delivered and "everything" slices are decided by the delivery link below, so no
                // status is excluded here: a delivered job may legitimately still read "Completed".
                _ => ordersQuery
            };

        if (request.CustomerId.HasValue) ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId);
        if (request.ItemId.HasValue) ordersQuery = ordersQuery.Where(o => o.ItemId == request.ItemId);
        if (request.Priority.HasValue) ordersQuery = ordersQuery.Where(o => o.Priority == request.Priority);

        var orders = await ordersQuery.ToListAsync(cancellationToken);

        orders = request.View switch
        {
            // A delivered job has left the floor: it is NOT part of the ordinary view, whether or not
            // its own status was ever advanced.
            ProductionFloorView.Open or ProductionFloorView.Closed
                => orders.Where(o => !deliveredOrderIds.Contains(o.Id)).ToList(),
            ProductionFloorView.Delivered
                => orders.Where(o => deliveredOrderIds.Contains(o.Id)).ToList(),
            _ => orders
        };

        if (orders.Count == 0) return new List<ProductionFloorRowDto>();

        var orderIds = orders.Select(o => o.Id).ToList();

        // Only the OPEN stages matter for running jobs. When a job has already finished (Delivered or Closed),
        // it has no open stage, so we also read its last COMPLETED stage so the row can show the final stage
        // it finished on — while a stage filter still requires the job to be in that stage right now.
        var allStageExecs = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId))
            .ToListAsync(cancellationToken);

        var openStageByOrder = allStageExecs
            .Where(s => s.Status == StageExecutionStatus.Pending || s.Status == StageExecutionStatus.InProgress)
            .GroupBy(s => s.ProductionOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).First());

        var lastCompletedStageByOrder = allStageExecs
            .Where(s => s.Status == StageExecutionStatus.Completed)
            .GroupBy(s => s.ProductionOrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).ThenByDescending(s => s.CompletedAtUtc).First());

        var currentStageByOrder = new Dictionary<Guid, ProductionOrderStageExecution>();
        foreach (var o in orders)
        {
            if (openStageByOrder.TryGetValue(o.Id, out var openStage))
                currentStageByOrder[o.Id] = openStage;
            else if (lastCompletedStageByOrder.TryGetValue(o.Id, out var completedStage))
                currentStageByOrder[o.Id] = completedStage;
        }

        if (request.StageDefinitionId.HasValue)
        {
            // When the user picks a specific production stage, ONLY orders that are currently in that stage
            // (or finished on that stage in the delivered/closed slice) may appear — never an order that
            // has not reached it or has already moved past it.
            var matchingOrderIds = currentStageByOrder
                .Where(kv => kv.Value.StageDefinitionId == request.StageDefinitionId.Value)
                .Select(kv => kv.Key)
                .ToHashSet();
            orders = orders.Where(o => matchingOrderIds.Contains(o.Id)).ToList();
            if (orders.Count == 0) return new List<ProductionFloorRowDto>();
        }

        var stageDefIds = currentStageByOrder.Values.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageDefs = await _db.ProductionStageDefinitions.AsNoTracking()
            .Where(d => stageDefIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => orders.Select(o => o.CustomerId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var now = DateTime.UtcNow;

        // The delivery history of one order: what it was handed over on, when, and how much. KG and
        // Meter are summed separately - a delivery of 100 KG and 40 M is not "140" of anything.
        string? DeliveryNumbersOf(Guid orderId) => deliveriesByOrder.TryGetValue(orderId, out var ids)
            ? string.Join(", ", ids.Select(id => deliveredDocs.FirstOrDefault(d => d.Id == id)?.DeliveryNumber)
                .Where(n => n is not null).Distinct())
            : null;

        DateTime? LastDeliveryDateOf(Guid orderId) => deliveriesByOrder.TryGetValue(orderId, out var ids)
            ? ids.Select(id => deliveredDocs.FirstOrDefault(d => d.Id == id)?.DeliveryDate)
                .Where(d => d.HasValue).Max()
            : null;

        decimal? DeliveredKgOf(Guid orderId) => deliveriesByOrder.TryGetValue(orderId, out var ids)
            ? deliveredLines
                .Where(l => ids.Contains(l.DeliveryId) && l.ProductionOrderId == orderId && l.QuantityKg.HasValue)
                .Sum(l => l.QuantityKg!.Value)
            : null;

        decimal? DeliveredMeterOf(Guid orderId) => deliveriesByOrder.TryGetValue(orderId, out var ids)
            ? deliveredLines
                .Where(l => ids.Contains(l.DeliveryId) && l.ProductionOrderId == orderId && l.QuantityMeter.HasValue)
                .Sum(l => l.QuantityMeter!.Value)
            : null;

        var rows = orders.Select(o =>
        {
            currentStageByOrder.TryGetValue(o.Id, out var stage);
            ProductionStageDefinition? def = null;
            if (stage is not null)
                stageDefs.TryGetValue(stage.StageDefinitionId, out def);

            var customer = customers.GetValueOrDefault(o.CustomerId);
            var item = items.GetValueOrDefault(o.ItemId);

            return new ProductionFloorRowDto
            {
                ProductionOrderId = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerCode = customer?.Code ?? string.Empty,
                CustomerName = customer?.Name ?? string.Empty,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.Name ?? string.Empty,
                Color = o.Color,
                RequestedQuantityKg = o.RequestedQuantityKg,
                RequestedQuantityMeter = o.RequestedQuantityMeter,
                StageDefinitionId = stage?.StageDefinitionId,
                CurrentStageName = def?.Name ?? string.Empty,
                CurrentStageStatus = stage?.Status ?? StageExecutionStatus.Pending,
                StageExecutionId = stage?.Id,
                StageSequence = stage?.Sequence,
                OrderStatus = o.Status,
                Priority = o.Priority,
                StageStartedAtUtc = stage?.StartedAtUtc,
                MinutesInStage = stage?.Status == StageExecutionStatus.InProgress && stage.StartedAtUtc.HasValue
                    ? (now - stage.StartedAtUtc.Value).TotalMinutes
                    : null,
                BaselineKg = stage?.BaselineKg,
                BaselineMeter = stage?.BaselineMeter,
                InputKg = stage?.InputKg,
                InputMeter = stage?.InputMeter,
                OutputKg = stage?.OutputKg,
                OutputMeter = stage?.OutputMeter,
                LossKg = stage?.LossKg,
                LossMeter = stage?.LossMeter,
                LossPercentKg = stage?.LossPercentKg,
                LossPercentMeter = stage?.LossPercentMeter,
                Operator = stage?.Operator,
                AwaitingStart = stage is null && o.Status is ProductionOrderStatus.RawAllocated or ProductionOrderStatus.InProduction or ProductionOrderStatus.Draft,
                CustomerReference = o.CustomerReference,
                Notes = o.Notes,
                IsDelivered = deliveriesByOrder.ContainsKey(o.Id),
                DeliveryNumbers = DeliveryNumbersOf(o.Id),
                LastDeliveryDate = LastDeliveryDateOf(o.Id),
                DeliveredQuantityKg = DeliveredKgOf(o.Id),
                DeliveredQuantityMeter = DeliveredMeterOf(o.Id)
            };
        }).ToList();

        // An order with no stage at all is waiting for the floor to start it - it belongs on this
        // screen, so it is kept rather than filtered away; delivered/closed orders are also kept in their view.
        rows = rows
            .Where(r => !string.IsNullOrEmpty(r.CurrentStageName) || r.AwaitingStart || r.IsDelivered
                        || r.OrderStatus is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            rows = rows
                .Where(r => r.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.CustomerCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.ItemCode.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Ordered by ATTENTION NEEDED, not by enum value: priority decides first, then how long the
        // job has been sitting on its stage. This is the single place that ordering is decided, and
        // the same rule the tests pin down.
        return rows
            .OrderByDescending(r => ProductionFloorSorting.Urgency(r.Priority, r.MinutesInStage))
            .ThenByDescending(r => r.MinutesInStage ?? 0)
            .ThenBy(r => r.OrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Urgency order for the floor screen - the single definition of what "needs attention first"
/// means. Priority dominates; time on the stage breaks ties within a priority. The waiting time
/// is capped at 3 hours so a job that has been open for days can never mask a genuinely urgent
/// one, which is why the weight is multiplied by 10.
/// </summary>
public static class ProductionFloorSorting
{
    public static int Urgency(ProductionPriority priority, double? minutesInStage)
    {
        var weight = priority switch
        {
            ProductionPriority.Urgent => 3,
            ProductionPriority.High => 2,
            ProductionPriority.Normal => 1,
            _ => 0
        };
        // Hours waiting, capped at 3 so an ancient Normal job cannot outrank an Urgent one outright.
        var hours = minutesInStage is null ? 0 : Math.Min(minutesInStage.Value / 60d, 3d);
        return (int)Math.Round(weight * 10 + hours);
    }
}