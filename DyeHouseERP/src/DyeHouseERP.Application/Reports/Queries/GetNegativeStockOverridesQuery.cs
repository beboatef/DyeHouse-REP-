using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Reports.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Reports.Queries;

public record GetNegativeStockOverridesQuery(DateTime? From = null, DateTime? To = null) : IRequest<List<NegativeStockOverrideDto>>;

public class GetNegativeStockOverridesQueryHandler : IRequestHandler<GetNegativeStockOverridesQuery, List<NegativeStockOverrideDto>>
{
    private readonly IApplicationDbContext _db;
    public GetNegativeStockOverridesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<NegativeStockOverrideDto>> Handle(GetNegativeStockOverridesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.NegativeStockOverrides.AsNoTracking().AsQueryable();
        if (request.From.HasValue) query = query.Where(o => o.ApprovedAtUtc >= request.From);
        if (request.To.HasValue) query = query.Where(o => o.ApprovedAtUtc <= request.To);

        var overrides = await query.OrderByDescending(o => o.ApprovedAtUtc).ToListAsync(cancellationToken);
        if (overrides.Count == 0) return new List<NegativeStockOverrideDto>();

        var messages = await _db.RawMessages.AsNoTracking().Where(m => overrides.Select(o => o.RawMessageId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().Where(c => overrides.Select(o => o.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => overrides.Select(o => o.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        return overrides.Select(o => new NegativeStockOverrideDto
        {
            Id = o.Id, ApprovedAtUtc = o.ApprovedAtUtc,
            MessageNumber = messages.GetValueOrDefault(o.RawMessageId)?.MessageNumber ?? "",
            CustomerCode = customers.GetValueOrDefault(o.CustomerId)?.Code ?? "",
            ItemCode = items.GetValueOrDefault(o.ItemId)?.Code ?? "",
            RequestedQuantity = o.RequestedQuantity, BalanceBefore = o.BalanceBefore, ResultingBalance = o.ResultingBalance,
            Reason = o.Reason, RequestedBy = o.RequestedBy, ApprovedBy = o.ApprovedBy
        }).ToList();
    }
}
