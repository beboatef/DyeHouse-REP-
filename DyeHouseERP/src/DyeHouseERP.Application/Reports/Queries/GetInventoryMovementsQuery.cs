using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Reports.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Reports.Queries;

/// <summary>
/// The inventory ledger itself (spec sections 10, 14 "Movements", 48 "Warehouse movements"
/// and "Customer raw material movement"). Balances are never stored - this is the source
/// they are all summed from, so this screen is the authoritative record of every stock change.
/// </summary>
public record GetInventoryMovementsQuery(
    Guid? CustomerId = null,
    Guid? ItemId = null,
    Guid? WarehouseId = null,
    Guid? RawMessageId = null,
    Guid? ProductionOrderId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Limit = 500) : IRequest<List<InventoryMovementDto>>;

public class GetInventoryMovementsQueryHandler
    : IRequestHandler<GetInventoryMovementsQuery, List<InventoryMovementDto>>
{
    private readonly IApplicationDbContext _db;
    public GetInventoryMovementsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<InventoryMovementDto>> Handle(GetInventoryMovementsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.InventoryTransactions.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue) query = query.Where(t => t.CustomerId == request.CustomerId);
        if (request.ItemId.HasValue) query = query.Where(t => t.ItemId == request.ItemId);
        if (request.WarehouseId.HasValue) query = query.Where(t => t.WarehouseId == request.WarehouseId);
        if (request.RawMessageId.HasValue) query = query.Where(t => t.RawMessageId == request.RawMessageId);
        if (request.ProductionOrderId.HasValue) query = query.Where(t => t.ProductionOrderId == request.ProductionOrderId);
        if (request.From.HasValue) query = query.Where(t => t.TransactionDate >= request.From);
        if (request.To.HasValue) query = query.Where(t => t.TransactionDate <= request.To);

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAtUtc)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        var customerIds = transactions.Select(t => t.CustomerId).Distinct().ToList();
        var itemIds = transactions.Select(t => t.ItemId).Distinct().ToList();
        var warehouseIds = transactions.Select(t => t.WarehouseId).Distinct().ToList();
        var messageIds = transactions.Where(t => t.RawMessageId.HasValue).Select(t => t.RawMessageId!.Value).Distinct().ToList();
        var orderIds = transactions.Where(t => t.ProductionOrderId.HasValue).Select(t => t.ProductionOrderId!.Value).Distinct().ToList();

        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name }).ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name }).ToDictionaryAsync(i => i.Id, i => (i.Code, i.Name), cancellationToken);
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking().Where(m => messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.MessageNumber, cancellationToken);
        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        return transactions.Select(t =>
        {
            var customer = customers.TryGetValue(t.CustomerId, out var c) ? c : (Code: "", Name: "");
            var item = items.TryGetValue(t.ItemId, out var i) ? i : (Code: "", Name: "");
            var sign = (int)t.Direction;

            return new InventoryMovementDto
            {
                Id = t.Id,
                SourceDocumentType = t.SourceDocumentType.ToString(),
                SourceDocumentNumber = t.SourceDocumentNumber,
                SourceDocumentId = t.SourceDocumentId,
                TransactionDate = t.TransactionDate,
                WarehouseId = t.WarehouseId,
                WarehouseName = warehouses.GetValueOrDefault(t.WarehouseId) ?? string.Empty,
                CustomerId = t.CustomerId,
                CustomerCode = customer.Code,
                CustomerName = customer.Name,
                ItemId = t.ItemId,
                ItemCode = item.Code,
                ItemName = item.Name,
                RawMessageId = t.RawMessageId,
                MessageNumber = t.RawMessageId.HasValue ? messages.GetValueOrDefault(t.RawMessageId.Value) : null,
                ProductionOrderId = t.ProductionOrderId,
                OrderNumber = t.ProductionOrderId.HasValue ? orders.GetValueOrDefault(t.ProductionOrderId.Value) : null,
                QuantityKg = t.QuantityKg,
                QuantityMeter = t.QuantityMeter,
                Direction = t.Direction,
                SignedQuantityKg = t.QuantityKg.HasValue ? t.QuantityKg.Value * sign : null,
                SignedQuantityMeter = t.QuantityMeter.HasValue ? t.QuantityMeter.Value * sign : null,
                CreatedBy = t.CreatedBy,
                CreatedAtUtc = t.CreatedAtUtc,
                ReversesTransactionId = t.ReversesTransactionId
            };
        }).ToList();
    }
}
