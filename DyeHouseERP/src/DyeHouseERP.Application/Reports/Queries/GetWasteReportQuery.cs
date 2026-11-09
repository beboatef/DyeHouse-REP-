using DyeHouseERP.Application.Common;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Reports.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Reports.Queries;

/// <summary>
/// الهالك report: the waste each Job Order actually produced, DERIVED from its four streams.
///
/// Approved policy (owner decision): waste stays a COMPUTED LOSS and is never a posted balance. This
/// handler is therefore strictly read-only - it posts no ledger row, creates no document and touches
/// no warehouse balance. Its figures come from the same definition the delivery statement prints
/// (<see cref="WasteMath"/>), and it resolves the produced quantity the same way, so the report and
/// the printed permit can never quote different denominators for one order.
/// </summary>
public record GetWasteReportQuery(
    Guid? CustomerId = null,
    Guid? ProductionOrderId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Limit = 500) : IRequest<WasteReportDto>;

public class GetWasteReportQueryHandler : IRequestHandler<GetWasteReportQuery, WasteReportDto>
{
    private readonly IApplicationDbContext _db;

    public GetWasteReportQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<WasteReportDto> Handle(GetWasteReportQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionOrders.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue) query = query.Where(o => o.CustomerId == request.CustomerId);
        if (request.ProductionOrderId.HasValue) query = query.Where(o => o.Id == request.ProductionOrderId.Value);
        if (request.From.HasValue) query = query.Where(o => o.OrderDate >= request.From);
        if (request.To.HasValue) query = query.Where(o => o.OrderDate <= request.To);

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenBy(o => o.OrderNumber)
            .Take(request.Limit)
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, o.CustomerId, o.ItemId, o.RequestedQuantityKg })
            .ToListAsync(cancellationToken);

        if (orders.Count == 0) return new WasteReportDto();

        var orderIds = orders.Select(o => o.Id).ToList();

        // Ready-goods transfers: cancelled documents keep their history but must not supply the produced
        // figure, exactly as on the delivery statement.
        var transfers = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => orderIds.Contains(t.ProductionOrderId) && t.Status == ReadyGoodsTransferStatus.Posted)
            .Select(t => new { t.ProductionOrderId, t.Category, t.QuantityKg })
            .ToListAsync(cancellationToken);

        // Waste is based on FIRST-GRADE output. Second grade and separates are their own streams and are
        // subtracted from the raw side, so counting their transfers here would deduct them twice. A null
        // category is retained for legacy transfers created before categories existed.
        var firstGradeKg = transfers
            .Where(t => t.Category == ReadyGoodsCategory.FirstGrade || t.Category == null)
            .GroupBy(t => t.ProductionOrderId)
            .ToDictionary(
                g => g.Key,
                g => g.Any(t => t.QuantityKg.HasValue) ? g.Sum(t => t.QuantityKg) : null);

        var ordersWithPostedTransfers = transfers.Select(t => t.ProductionOrderId).ToHashSet();

        var stageRows = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId))
            .Select(s => new
            {
                s.ProductionOrderId, s.Sequence, s.CompletedAtUtc, s.Status,
                s.OutputKg, s.SeparatesKg, s.SecondGradeKg
            })
            .ToListAsync(cancellationToken);

        var lastCompletedOutput = stageRows
            .Where(s => s.Status == StageExecutionStatus.Completed)
            .GroupBy(s => s.ProductionOrderId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Sequence).ThenByDescending(s => s.CompletedAtUtc)
                      .First().OutputKg);

        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();

        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name })
            .ToDictionaryAsync(i => i.Id, i => (i.Code, i.Name), cancellationToken);

        var rows = orders.Select(order =>
        {
            var stages = stageRows.Where(s => s.ProductionOrderId == order.Id).ToList();

            // Produced figure resolution, identical to the delivery statement: the posted first-grade
            // transfer when there is one, otherwise the last completed operation's output - but NOT when
            // this order has posted transfers of other categories only (that output would double-subtract
            // the second grade / separates already recorded on the stages).
            decimal? producedKg;
            string? note = null;
            if (firstGradeKg.TryGetValue(order.Id, out var firstGrade))
            {
                producedKg = firstGrade;
                if (producedKg is null) note = "No first-grade ready weight was posted for this order.";
            }
            else if (ordersWithPostedTransfers.Contains(order.Id))
            {
                producedKg = null;
                note = "This order posted only second-grade/separates stock, so it has no first-grade weight to measure against.";
            }
            else
            {
                producedKg = lastCompletedOutput.GetValueOrDefault(order.Id);
                if (producedKg is null) note = "No first-grade ready weight was posted and no completed operation recorded an output.";
            }

            var separatesKg = Sum(stages.Select(s => s.SeparatesKg));
            var secondGradeKg = Sum(stages.Select(s => s.SecondGradeKg));
            var wasteKg = WasteMath.Kg(order.RequestedQuantityKg, producedKg, separatesKg, secondGradeKg);

            if (wasteKg is null && note is null)
                note = order.RequestedQuantityKg is null
                    ? "The order has no original raw weight recorded."
                    : "The recorded streams would make the waste negative - the figures overlap or the original raw weight is understated.";

            var customer = customers.TryGetValue(order.CustomerId, out var c) ? c : (Code: "", Name: "");
            var item = items.TryGetValue(order.ItemId, out var i) ? i : (Code: "", Name: "");

            return new WasteReportRowDto
            {
                ProductionOrderId = order.Id,
                ProductionOrderNumber = order.OrderNumber,
                ProductionOrderDate = order.OrderDate,
                CustomerCode = customer.Code,
                CustomerName = customer.Name,
                ItemCode = item.Code,
                ItemName = item.Name,
                OriginalRawKg = order.RequestedQuantityKg,
                FirstGradeReadyKg = producedKg,
                SecondGradeKg = secondGradeKg,
                SeparatesKg = separatesKg,
                WasteKg = wasteKg,
                WastePercentKg = WasteMath.Percent(wasteKg, order.RequestedQuantityKg),
                Note = wasteKg is null ? note : null
            };
        }).ToList();

        var totalOriginal = Sum(rows.Select(r => r.OriginalRawKg));
        var totalWaste = Sum(rows.Select(r => r.WasteKg));

        return new WasteReportDto
        {
            Rows = rows,
            TotalOriginalRawKg = totalOriginal,
            TotalFirstGradeReadyKg = Sum(rows.Select(r => r.FirstGradeReadyKg)),
            TotalSecondGradeKg = Sum(rows.Select(r => r.SecondGradeKg)),
            TotalSeparatesKg = Sum(rows.Select(r => r.SeparatesKg)),
            TotalWasteKg = totalWaste,
            TotalWastePercentKg = WasteMath.Percent(totalWaste, totalOriginal)
        };
    }

    /// <summary>Sum of a nullable stream, or null when nothing was recorded anywhere.</summary>
    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var list = values.ToList();
        return list.Count == 0 || list.All(v => v is null) ? null : list.Sum(v => v ?? 0m);
    }
}
