using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Queries;

/// <summary>
/// The register of ready-goods-to-raw movements (spec section 29) - every authorized, reasoned move of
/// finished stock back into raw material stock, newest first. Read-only: the documents post their
/// ledger rows once and are then immutable.
/// </summary>
public record GetReadyGoodsRawReturnsQuery(Guid? CustomerId = null) : IRequest<List<ReadyGoodsRawReturnDto>>;

public class GetReadyGoodsRawReturnsQueryHandler : IRequestHandler<GetReadyGoodsRawReturnsQuery, List<ReadyGoodsRawReturnDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReadyGoodsRawReturnsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReadyGoodsRawReturnDto>> Handle(
        GetReadyGoodsRawReturnsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ReadyGoodsRawReturns.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(m => m.CustomerId == request.CustomerId.Value);

        var movements = await query
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (movements.Count == 0) return new List<ReadyGoodsRawReturnDto>();

        var orderIds = movements.Select(m => m.ProductionOrderId).Distinct().ToList();
        var customerIds = movements.Select(m => m.CustomerId).Distinct().ToList();
        var itemIds = movements.Select(m => m.ItemId).Distinct().ToList();
        var warehouseIds = movements.Select(m => m.SourceWarehouseId)
            .Concat(movements.Select(m => m.DestinationWarehouseId)).Distinct().ToList();
        var messageIds = movements.Select(m => m.DestinationRawMessageId).Distinct().ToList();

        var orders = await _db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
        var warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.MessageNumber, cancellationToken);

        return movements.Select(m => new ReadyGoodsRawReturnDto
        {
            Id = m.Id,
            MovementNumber = m.MovementNumber,
            Category = m.Category.ToString(),
            MovementDate = m.MovementDate,
            ProductionOrderId = m.ProductionOrderId,
            ProductionOrderNumber = orders.GetValueOrDefault(m.ProductionOrderId) ?? string.Empty,
            CustomerId = m.CustomerId,
            CustomerCode = customers.GetValueOrDefault(m.CustomerId) ?? string.Empty,
            ItemId = m.ItemId,
            ItemCode = items.GetValueOrDefault(m.ItemId) ?? string.Empty,
            SourceWarehouseId = m.SourceWarehouseId,
            SourceWarehouseName = warehouses.GetValueOrDefault(m.SourceWarehouseId) ?? string.Empty,
            DestinationRawMessageId = m.DestinationRawMessageId,
            DestinationMessageNumber = messages.GetValueOrDefault(m.DestinationRawMessageId) ?? string.Empty,
            DestinationWarehouseId = m.DestinationWarehouseId,
            DestinationWarehouseName = warehouses.GetValueOrDefault(m.DestinationWarehouseId) ?? string.Empty,
            QuantityKg = m.QuantityKg,
            QuantityMeter = m.QuantityMeter,
            Reason = m.Reason,
            Notes = m.Notes,
            CreatedBy = m.CreatedBy,
            CreatedAtUtc = m.CreatedAtUtc
        }).ToList();
    }
}

public record GetReadyGoodsTransfersQuery : IRequest<List<ReadyGoodsTransferDto>>;

public class GetReadyGoodsTransfersQueryHandler : IRequestHandler<GetReadyGoodsTransfersQuery, List<ReadyGoodsTransferDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReadyGoodsTransfersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReadyGoodsTransferDto>> Handle(GetReadyGoodsTransfersQuery request, CancellationToken cancellationToken)
    {
        var transfers = await _db.ReadyGoodsTransfers.AsNoTracking().OrderByDescending(t => t.TransferDate).ToListAsync(cancellationToken);
        if (transfers.Count == 0) return new List<ReadyGoodsTransferDto>();

        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => transfers.Select(t => t.ProductionOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().Where(c => transfers.Select(t => t.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => transfers.Select(t => t.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => transfers.Select(t => t.WarehouseId).Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        // Available quantity per lot, live from the ledger (spec section 29): the ready dimension is
        // (RawMessageId IS NULL, ProductionOrderId, ItemId), so the same figures the delivery and the
        // ready-to-raw movement act on are what this register reports - never a stored copy.
        var orderIdsForLots = transfers.Select(t => t.ProductionOrderId).Distinct().ToList();
        var itemIdsForLots = transfers.Select(t => t.ItemId).Distinct().ToList();
        var lotRows = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == null && t.ProductionOrderId != null
                     && orderIdsForLots.Contains(t.ProductionOrderId!.Value)
                     && itemIdsForLots.Contains(t.ItemId))
            .Select(t => new { t.ProductionOrderId, t.ItemId, t.ReadyGoodsCategory, t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        // The register reads each transfer's stage back with its IDENTITY (sequence and
        // name), not a bare id (spec section 23). A historical row with no link simply
        // renders as "no stage" rather than being dropped.
        var stageIds = transfers.Where(t => t.StageExecutionId.HasValue)
            .Select(t => t.StageExecutionId!.Value).Distinct().ToList();
        var stages = stageIds.Count == 0
            ? new Dictionary<Guid, ProductionOrderStageExecution>()
            : await _db.ProductionOrderStageExecutions.AsNoTracking()
                .Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);

        var definitionIds = stages.Values.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageNames = definitionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ProductionStageDefinitions.AsNoTracking()
                .Where(d => definitionIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        ProductionOrderStageExecution? StageOf(Guid? id) =>
            id.HasValue && stages.TryGetValue(id.Value, out var s) ? s : null;

        return transfers.Select(t =>
        {
            var stage = StageOf(t.StageExecutionId);
            return new ReadyGoodsTransferDto
            {
                Id = t.Id, TransferNumber = t.TransferNumber, TransferDate = t.TransferDate,
                ProductionOrderId = t.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(t.ProductionOrderId)?.OrderNumber ?? "",
                CustomerId = t.CustomerId, CustomerCode = customers.GetValueOrDefault(t.CustomerId)?.Code ?? "",
                CustomerName = customers.GetValueOrDefault(t.CustomerId)?.Name ?? "",
                ItemId = t.ItemId, ItemCode = items.GetValueOrDefault(t.ItemId)?.Code ?? "",
                ItemName = items.GetValueOrDefault(t.ItemId)?.Name ?? "",
                WarehouseId = t.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(t.WarehouseId) ?? "",
                AvailableQuantityKg = lotRows.Where(r => r.ProductionOrderId == t.ProductionOrderId && r.ItemId == t.ItemId && (r.ReadyGoodsCategory ?? DyeHouseERP.Domain.Enums.ReadyGoodsCategory.FirstGrade) == t.Category)
                    .Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction),
                AvailableQuantityMeter = lotRows.Where(r => r.ProductionOrderId == t.ProductionOrderId && r.ItemId == t.ItemId && (r.ReadyGoodsCategory ?? DyeHouseERP.Domain.Enums.ReadyGoodsCategory.FirstGrade) == t.Category)
                    .Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction),
                Color = orders.GetValueOrDefault(t.ProductionOrderId)?.Color,
                QuantityKg = t.QuantityKg, QuantityMeter = t.QuantityMeter, PieceCount = t.PieceCount,
                StageExecutionId = t.StageExecutionId,
                StageSequence = stage?.Sequence,
                StageName = stage is null ? null : stageNames.GetValueOrDefault(stage.StageDefinitionId),
                StageOutputKg = stage?.OutputKg,
                StageOutputMeter = stage?.OutputMeter,
                Status = t.Status.ToString()
            };
        }).ToList();
    }
}

/// <summary>
/// Ready stock balance, always derived live from the ledger (spec sections
/// 18, 30) - grouped by Production Order so it stays traceable back to
/// customer/item/color/raw origin, never a flat "item total".
/// </summary>
public record GetReadyGoodsBalanceQuery(Guid? CustomerId = null) : IRequest<List<ReadyGoodsBalanceDto>>;

public class GetReadyGoodsBalanceQueryHandler : IRequestHandler<GetReadyGoodsBalanceQuery, List<ReadyGoodsBalanceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetReadyGoodsBalanceQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ReadyGoodsBalanceDto>> Handle(GetReadyGoodsBalanceQuery request, CancellationToken cancellationToken)
    {
        var query = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == null && t.ProductionOrderId != null);

        if (request.CustomerId.HasValue) query = query.Where(t => t.CustomerId == request.CustomerId);

        var grouped = await query
            .GroupBy(t => new { ProductionOrderId = t.ProductionOrderId!.Value, t.ItemId, t.CustomerId, Category = t.ReadyGoodsCategory ?? DyeHouseERP.Domain.Enums.ReadyGoodsCategory.FirstGrade })
            .Select(g => new
            {
                g.Key.ProductionOrderId, g.Key.ItemId, g.Key.CustomerId, g.Key.Category,
                Kg = g.Sum(t => t.Direction == TransactionDirection.In ? (t.QuantityKg ?? 0) : -(t.QuantityKg ?? 0)),
                Meter = g.Sum(t => t.Direction == TransactionDirection.In ? (t.QuantityMeter ?? 0) : -(t.QuantityMeter ?? 0))
            })
            .Where(g => g.Kg > 0 || g.Meter > 0)
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0) return new List<ReadyGoodsBalanceDto>();

        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => grouped.Select(g => g.ProductionOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().Where(c => grouped.Select(g => g.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => grouped.Select(g => g.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        return grouped.Select(g => new ReadyGoodsBalanceDto
        {
            ProductionOrderId = g.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(g.ProductionOrderId)?.OrderNumber ?? "",
            CustomerId = g.CustomerId, CustomerCode = customers.GetValueOrDefault(g.CustomerId)?.Code ?? "", CustomerName = customers.GetValueOrDefault(g.CustomerId)?.Name ?? "",
            ItemId = g.ItemId, ItemCode = items.GetValueOrDefault(g.ItemId)?.Code ?? "", ItemName = items.GetValueOrDefault(g.ItemId)?.Name ?? "",
            Category = g.Category.ToString(),
            Color = orders.GetValueOrDefault(g.ProductionOrderId)?.Color,
            RemainingKg = g.Kg, RemainingMeter = g.Meter
        }).ToList();
    }
}
