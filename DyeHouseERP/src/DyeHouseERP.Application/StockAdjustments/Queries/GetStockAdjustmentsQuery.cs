using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.StockAdjustments.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.StockAdjustments.Queries;

public record GetStockAdjustmentsQuery(Guid? CustomerId = null) : IRequest<List<StockAdjustmentDto>>;

public class GetStockAdjustmentsQueryHandler : IRequestHandler<GetStockAdjustmentsQuery, List<StockAdjustmentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetStockAdjustmentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<StockAdjustmentDto>> Handle(GetStockAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.StockAdjustments.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(a => a.CustomerId == request.CustomerId);

        var adjustments = await query.OrderByDescending(a => a.AdjustmentDate).ToListAsync(cancellationToken);
        if (adjustments.Count == 0) return new List<StockAdjustmentDto>();

        var customers = await _db.Customers.AsNoTracking().Where(c => adjustments.Select(a => a.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => adjustments.Select(a => a.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking().Where(m => adjustments.Select(a => a.RawMessageId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        return adjustments.Select(a => new StockAdjustmentDto
        {
            Id = a.Id, AdjustmentNumber = a.AdjustmentNumber, AdjustmentDate = a.AdjustmentDate,
            CustomerId = a.CustomerId, CustomerCode = customers.GetValueOrDefault(a.CustomerId)?.Code ?? "", CustomerName = customers.GetValueOrDefault(a.CustomerId)?.Name ?? "",
            ItemId = a.ItemId, ItemCode = items.GetValueOrDefault(a.ItemId)?.Code ?? "", ItemName = items.GetValueOrDefault(a.ItemId)?.Name ?? "",
            RawMessageId = a.RawMessageId, MessageNumber = messages.GetValueOrDefault(a.RawMessageId)?.MessageNumber ?? "",
            Type = a.Type, QuantityBeforeKg = a.QuantityBeforeKg, QuantityBeforeMeter = a.QuantityBeforeMeter,
            AdjustmentQuantityKg = a.AdjustmentQuantityKg, AdjustmentQuantityMeter = a.AdjustmentQuantityMeter,
            QuantityAfterKg = a.QuantityAfterKg, QuantityAfterMeter = a.QuantityAfterMeter,
            Reason = a.Reason, Notes = a.Notes, ApprovedBy = a.ApprovedBy, CreatedBy = a.CreatedBy, CreatedAtUtc = a.CreatedAtUtc
        }).ToList();
    }
}
