using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Queries;

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
                ItemId = t.ItemId, ItemCode = items.GetValueOrDefault(t.ItemId)?.Code ?? "",
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
            .GroupBy(t => new { ProductionOrderId = t.ProductionOrderId!.Value, t.ItemId, t.CustomerId })
            .Select(g => new
            {
                g.Key.ProductionOrderId, g.Key.ItemId, g.Key.CustomerId,
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
            Color = orders.GetValueOrDefault(g.ProductionOrderId)?.Color,
            RemainingKg = g.Kg, RemainingMeter = g.Meter
        }).ToList();
    }
}
