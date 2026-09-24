using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerPortal.Queries;

public class ProductionFloorRowDto
{
    public Guid ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? RequestedQuantityKg { get; set; }
    public decimal? RequestedQuantityMeter { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public StageExecutionStatus CurrentStageStatus { get; set; }
    public ProductionOrderStatus OrderStatus { get; set; }
    public ProductionPriority Priority { get; set; }
    public DateTime? StageStartedAtUtc { get; set; }
    public double? MinutesInStage { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Production floor dashboard feed (spec section 37) - one row per active
/// Production Order showing its CURRENT stage (first Pending/InProgress in
/// sequence order). Filterable by stage/status/customer/item/date/priority
/// at the query layer so the floor screen can stay a thin table.
/// </summary>
public record GetProductionFloorQuery(
    Guid? StageDefinitionId = null, ProductionOrderStatus? Status = null, Guid? CustomerId = null,
    Guid? ItemId = null, ProductionPriority? Priority = null) : IRequest<List<ProductionFloorRowDto>>;

public class GetProductionFloorQueryHandler : IRequestHandler<GetProductionFloorQuery, List<ProductionFloorRowDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionFloorQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionFloorRowDto>> Handle(GetProductionFloorQuery request, CancellationToken cancellationToken)
    {
        var ordersQuery = _db.ProductionOrders.AsNoTracking()
            .Where(o => o.Status == ProductionOrderStatus.InProduction || o.Status == ProductionOrderStatus.RawAllocated);

        if (request.Status.HasValue) ordersQuery = ordersQuery.Where(o => o.Status == request.Status);
        if (request.CustomerId.HasValue) ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId);
        if (request.ItemId.HasValue) ordersQuery = ordersQuery.Where(o => o.ItemId == request.ItemId);
        if (request.Priority.HasValue) ordersQuery = ordersQuery.Where(o => o.Priority == request.Priority);

        var orders = await ordersQuery.ToListAsync(cancellationToken);
        if (orders.Count == 0) return new List<ProductionFloorRowDto>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var stageExecs = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId)
                && (s.Status == StageExecutionStatus.Pending || s.Status == StageExecutionStatus.InProgress))
            .ToListAsync(cancellationToken);

        var currentStageByOrder = stageExecs
            .GroupBy(s => s.ProductionOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).First());

        if (request.StageDefinitionId.HasValue)
            currentStageByOrder = currentStageByOrder.Where(kv => kv.Value.StageDefinitionId == request.StageDefinitionId)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        var stageDefIds = currentStageByOrder.Values.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageDefs = await _db.ProductionStageDefinitions.AsNoTracking().Where(d => stageDefIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);

        var customers = await _db.Customers.AsNoTracking().Where(c => orders.Select(o => o.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => orders.Select(o => o.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        var now = DateTime.UtcNow;

        return orders
            .Where(o => currentStageByOrder.ContainsKey(o.Id))
            .Select(o =>
            {
                var stage = currentStageByOrder[o.Id];
                stageDefs.TryGetValue(stage.StageDefinitionId, out var def);
                return new ProductionFloorRowDto
                {
                    ProductionOrderId = o.Id, OrderNumber = o.OrderNumber,
                    CustomerCode = customers.GetValueOrDefault(o.CustomerId)?.Code ?? "",
                    ItemCode = items.GetValueOrDefault(o.ItemId)?.Code ?? "", Color = o.Color,
                    RequestedQuantityKg = o.RequestedQuantityKg, RequestedQuantityMeter = o.RequestedQuantityMeter,
                    CurrentStageName = def?.Name ?? "", CurrentStageStatus = stage.Status, OrderStatus = o.Status, Priority = o.Priority,
                    StageStartedAtUtc = stage.StartedAtUtc,
                    MinutesInStage = stage.StartedAtUtc.HasValue ? (now - stage.StartedAtUtc.Value).TotalMinutes : null,
                    Notes = o.Notes
                };
            })
            .OrderByDescending(r => r.Priority)
            .ToList();
    }
}
