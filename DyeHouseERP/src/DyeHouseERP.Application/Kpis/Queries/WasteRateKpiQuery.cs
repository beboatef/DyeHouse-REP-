using DyeHouseERP.Application.Common;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Waste rate per job order and per period (spec Part 1 metric 1). Stage
/// waste is the WasteKg definition on stage executions (input minus output
/// minus second grade minus separates, null when the KG streams are missing
/// or the difference would go negative), replicated here in SQL because
/// WasteKg itself is an unmapped computed property the database cannot
/// evaluate. rawKg is the order's RequestedQuantityKg. The ratio uses
/// WasteMath.Percent - the same definition as the waste report - so the two
/// screens can never quote different figures for the same order.
/// </summary>
public record GetWasteRateKpiQuery(DateTime? From = null, DateTime? To = null, Guid? CustomerId = null, Guid? ProductionOrderId = null)
    : IRequest<WasteRateKpiResult>, IKpiRange;

public class GetWasteRateKpiQueryValidator : KpiRangeValidator<GetWasteRateKpiQuery>;

public class GetWasteRateKpiQueryHandler : IRequestHandler<GetWasteRateKpiQuery, WasteRateKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetWasteRateKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<WasteRateKpiResult> Handle(GetWasteRateKpiQuery request, CancellationToken cancellationToken)
    {
        var ordersQuery = _db.ProductionOrders.AsNoTracking().AsQueryable();
        if (request.From.HasValue) ordersQuery = ordersQuery.Where(o => o.OrderDate >= request.From.Value);
        if (request.To.HasValue) ordersQuery = ordersQuery.Where(o => o.OrderDate <= request.To.Value);
        if (request.CustomerId.HasValue) ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId.Value);
        if (request.ProductionOrderId.HasValue) ordersQuery = ordersQuery.Where(o => o.Id == request.ProductionOrderId.Value);

        var orders = await ordersQuery
            .OrderBy(o => o.OrderDate)
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, o.CustomerId, RawKg = o.RequestedQuantityKg ?? 0m })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var wasteByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.ProductionOrderStageExecutions.AsNoTracking()
                .Where(e => orderIds.Contains(e.ProductionOrderId)
                    && e.InputKg != null && e.OutputKg != null
                    && e.InputKg.Value - e.OutputKg.Value - (e.SecondGradeKg ?? 0m) - (e.SeparatesKg ?? 0m) >= 0m)
                .GroupBy(e => e.ProductionOrderId)
                .Select(g => new
                {
                    OrderId = g.Key,
                    WasteKg = g.Sum(e => e.InputKg!.Value - e.OutputKg!.Value - (e.SecondGradeKg ?? 0m) - (e.SeparatesKg ?? 0m))
                })
                .ToDictionaryAsync(x => x.OrderId, x => x.WasteKg, cancellationToken);

        var customerCodes = await _db.Customers.AsNoTracking()
            .Where(c => orders.Select(o => o.CustomerId).Distinct().Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);

        var result = new WasteRateKpiResult
        {
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow
        };

        foreach (var o in orders)
        {
            var waste = wasteByOrder.GetValueOrDefault(o.Id);
            result.Orders.Add(new WasteRateOrderRow
            {
                ProductionOrderId = o.Id,
                OrderNumber = o.OrderNumber,
                OrderDate = o.OrderDate,
                CustomerCode = customerCodes.GetValueOrDefault(o.CustomerId, string.Empty),
                RawKg = o.RawKg,
                WasteKg = waste,
                WasteRatePct = WasteMath.Percent(waste, o.RawKg)
            });
        }

        result.TotalRawKg = result.Orders.Sum(o => o.RawKg);
        result.TotalWasteKg = result.Orders.Sum(o => o.WasteKg);
        result.OverallRatePct = WasteMath.Percent(result.TotalWasteKg, result.TotalRawKg);
        result.OrdersWithWaste = result.Orders.Count(o => o.WasteKg > 0);
        return result;
    }
}
