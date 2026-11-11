using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Average lead time per job order (spec Part 1 metric 2): from the issue date
/// (the raw-issue OUT ledger row for the order) to the first posted ready-goods
/// transfer date. Only orders with OrderDate in range that have BOTH dates are
/// counted; orders still in production are excluded, never guessed.
/// </summary>
public record GetLeadTimeKpiQuery(DateTime? From = null, DateTime? To = null, Guid? CustomerId = null)
    : IRequest<LeadTimeKpiResult>, IKpiRange;

public class GetLeadTimeKpiQueryValidator : KpiRangeValidator<GetLeadTimeKpiQuery>;

public class GetLeadTimeKpiQueryHandler : IRequestHandler<GetLeadTimeKpiQuery, LeadTimeKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetLeadTimeKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<LeadTimeKpiResult> Handle(GetLeadTimeKpiQuery request, CancellationToken cancellationToken)
    {
        var ordersQuery = _db.ProductionOrders.AsNoTracking().AsQueryable();
        if (request.From.HasValue) ordersQuery = ordersQuery.Where(o => o.OrderDate >= request.From.Value);
        if (request.To.HasValue) ordersQuery = ordersQuery.Where(o => o.OrderDate <= request.To.Value);
        if (request.CustomerId.HasValue) ordersQuery = ordersQuery.Where(o => o.CustomerId == request.CustomerId.Value);

        var orders = await ordersQuery
            .OrderBy(o => o.OrderDate)
            .Select(o => new { o.Id, o.OrderNumber })
            .ToListAsync(cancellationToken);

        var result = new LeadTimeKpiResult
        {
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow
        };
        if (orders.Count == 0) return result;

        var orderIds = orders.Select(o => o.Id).ToList();

        var issueDates = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductionOrderId != null
                && orderIds.Contains(t.ProductionOrderId.Value)
                && t.RawMessageId != null
                && t.Direction == TransactionDirection.Out)
            .GroupBy(t => t.ProductionOrderId!.Value)
            .Select(g => new { OrderId = g.Key, IssueDate = g.Min(t => t.TransactionDate) })
            .ToDictionaryAsync(x => x.OrderId, x => x.IssueDate, cancellationToken);

        var completionDates = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => orderIds.Contains(t.ProductionOrderId) && t.Status == ReadyGoodsTransferStatus.Posted)
            .GroupBy(t => t.ProductionOrderId)
            .Select(g => new { OrderId = g.Key, CompletionDate = g.Min(t => t.TransferDate) })
            .ToDictionaryAsync(x => x.OrderId, x => x.CompletionDate, cancellationToken);

        foreach (var o in orders)
        {
            if (!issueDates.TryGetValue(o.Id, out var issue) || !completionDates.TryGetValue(o.Id, out var completion))
                continue;
            result.Orders.Add(new LeadTimeOrderRow
            {
                ProductionOrderId = o.Id,
                OrderNumber = o.OrderNumber,
                IssueDate = issue,
                CompletionDate = completion,
                LeadTimeDays = Math.Round((completion - issue).TotalDays, 1)
            });
        }

        var days = result.Orders.Select(o => o.LeadTimeDays).ToList();
        result.FinishedOrders = result.Orders.Count;
        result.AverageLeadTimeDays = KpiQueryHelpers.AverageOrNull(days) is { } avg ? Math.Round(avg, 1) : null;
        result.MinLeadTimeDays = days.Count == 0 ? null : days.Min();
        result.MaxLeadTimeDays = days.Count == 0 ? null : days.Max();
        return result;
    }
}
