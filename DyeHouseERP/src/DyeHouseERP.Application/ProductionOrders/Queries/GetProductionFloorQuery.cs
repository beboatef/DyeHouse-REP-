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
}

/// <summary>
/// The factory floor feed (spec section 37): one row per LIVE Job Order.
///
/// "Live" means every order still physically on the floor - running, waiting for
/// its first stage, or paused. A paused job is not gone: the material is still
/// allocated to it and the supervisor has to see it, so it is listed and clearly
/// marked rather than silently dropped.
///
/// Stage names are resolved from the configurable stage definitions; nothing about
/// the route is hard-coded here.
/// </summary>
public record GetProductionFloorQuery(
    Guid? StageDefinitionId = null, ProductionOrderStatus? Status = null, Guid? CustomerId = null,
    Guid? ItemId = null, ProductionPriority? Priority = null, string? Search = null) : IRequest<List<ProductionFloorRowDto>>;

public class GetProductionFloorQueryHandler : IRequestHandler<GetProductionFloorQuery, List<ProductionFloorRowDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionFloorQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionFloorRowDto>> Handle(GetProductionFloorQuery request, CancellationToken cancellationToken)
    {
        var ordersQuery = _db.ProductionOrders.AsNoTracking()
            .Where(o => o.Status == ProductionOrderStatus.InProduction
                || o.Status == ProductionOrderStatus.RawAllocated
                || o.Status == ProductionOrderStatus.Paused);

        if (request.Status.HasValue) ordersQuery = ordersQuery.Where(o => o.Status == request.Status);
        if (request.CustomerId.HasValue) ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId);
        if (request.ItemId.HasValue) ordersQuery = ordersQuery.Where(o => o.ItemId == request.ItemId);
        if (request.Priority.HasValue) ordersQuery = ordersQuery.Where(o => o.Priority == request.Priority);

        var orders = await ordersQuery.ToListAsync(cancellationToken);
        if (orders.Count == 0) return new List<ProductionFloorRowDto>();

        var orderIds = orders.Select(o => o.Id).ToList();

        // Only the OPEN stages matter here: the one being worked on, or the one waiting to start.
        var stageExecs = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId)
                && (s.Status == StageExecutionStatus.Pending || s.Status == StageExecutionStatus.InProgress))
            .ToListAsync(cancellationToken);

        var currentStageByOrder = stageExecs
            .GroupBy(s => s.ProductionOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).First());

        if (request.StageDefinitionId.HasValue)
            currentStageByOrder = currentStageByOrder
                .Where(kv => kv.Value.StageDefinitionId == request.StageDefinitionId)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

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
                MinutesInStage = stage?.StartedAtUtc is null ? null : (now - stage.StartedAtUtc.Value).TotalMinutes,
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
                AwaitingStart = stage is null,
                CustomerReference = o.CustomerReference,
                Notes = o.Notes
            };
        }).ToList();

        // An order with no stage at all is waiting for the floor to start it - it belongs on this
        // screen, so it is kept rather than filtered away.
        rows = rows
            .Where(r => !string.IsNullOrEmpty(r.CurrentStageName) || r.AwaitingStart)
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