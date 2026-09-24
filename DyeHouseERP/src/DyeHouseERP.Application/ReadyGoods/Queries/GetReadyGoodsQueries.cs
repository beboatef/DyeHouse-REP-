using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReadyGoods.DTOs;
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

        return transfers.Select(t => new ReadyGoodsTransferDto
        {
            Id = t.Id, TransferNumber = t.TransferNumber, TransferDate = t.TransferDate,
            ProductionOrderId = t.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(t.ProductionOrderId)?.OrderNumber ?? "",
            CustomerId = t.CustomerId, CustomerCode = customers.GetValueOrDefault(t.CustomerId)?.Code ?? "",
            ItemId = t.ItemId, ItemCode = items.GetValueOrDefault(t.ItemId)?.Code ?? "",
            Color = orders.GetValueOrDefault(t.ProductionOrderId)?.Color,
            QuantityKg = t.QuantityKg, QuantityMeter = t.QuantityMeter, PieceCount = t.PieceCount
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
                Kg = g.Sum(t => (t.QuantityKg ?? 0) * (int)t.Direction),
                Meter = g.Sum(t => (t.QuantityMeter ?? 0) * (int)t.Direction)
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
