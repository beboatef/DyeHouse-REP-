using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Queries;

/// <summary>
/// The DELIVERY STATEMENT (بيان تسليم) of one permit (spec section 31).
///
/// It is a read-only document: building or printing it posts NO stock movement and changes no
/// document, so printing and reprinting are always free of side effects.
///
/// Per line it reports the job's own production accounting - the ORIGINAL raw quantity it started
/// from, what this delivery settles of it, what is still outstanding, the ready-goods quantity
/// handed over, the bale count, the scraps/separates and second-grade the operations recorded, and
/// the calculated waste. Every figure is kept in its own unit: KG and Meter are never converted into
/// one another, and the waste is computed within a unit only. A unit that was never recorded reports
/// null rather than a zero that would read as a real measurement.
///
/// The original Job Order DATE is read live from the order so a later partial-delivery statement
/// keeps showing when the job was actually taken in, and the original raw quantity is read live too -
/// it is fixed on the order and no delivery ever overwrites it.
/// </summary>
public record GetDeliveryStatementQuery(Guid DeliveryId) : IRequest<DeliveryStatementDto>;

public class GetDeliveryStatementQueryHandler : IRequestHandler<GetDeliveryStatementQuery, DeliveryStatementDto>
{
    private readonly IApplicationDbContext _db;
    public GetDeliveryStatementQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<DeliveryStatementDto> Handle(GetDeliveryStatementQuery request, CancellationToken cancellationToken)
    {
        var delivery = await _db.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        var lines = await _db.DeliveryLines.AsNoTracking()
            .Where(l => l.DeliveryId == delivery.Id)
            .ToListAsync(cancellationToken);

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == delivery.CustomerId, cancellationToken);

        var orderIds = lines.Select(l => l.ProductionOrderId).Distinct().ToList();
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();

        var orders = await _db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        // Scraps/separates and second grade belong to the OPERATIONS of the order, so they are summed
        // from the stage executions - read, never copied onto a delivery row that could then drift.
        var stageRows = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId))
            .Select(s => new
            {
                s.ProductionOrderId, s.SeparatesKg, s.SeparatesMeter,
                s.SecondGradeKg, s.SecondGradeMeter, s.RequiresDrawingNumber, s.DrawingNumber
            })
            .ToListAsync(cancellationToken);

        // What the job actually produced (the ready-goods quantity), resolved exactly the way the cost
        // rollup resolves its output denominator: the ready-goods transfer when there is one, otherwise
        // the last COMPLETED operation's output. The two screens can therefore never quote different
        // denominators for one order.
        var transfers = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => orderIds.Contains(t.ProductionOrderId))
            .Select(t => new { t.ProductionOrderId, t.QuantityKg, t.QuantityMeter })
            .ToListAsync(cancellationToken);

        var lastStageOutput = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId) && s.Status == StageExecutionStatus.Completed)
            .OrderByDescending(s => s.Sequence).ThenByDescending(s => s.CompletedAtUtc)
            .Select(s => new { s.ProductionOrderId, s.OutputKg, s.OutputMeter })
            .ToListAsync(cancellationToken);

        var remaining = await GetDeliveriesQueryHandler.ReadRemainingOriginalRawAsync(_db, orderIds, cancellationToken);

        string? primaryNumber = null;
        string? primaryCustomerCode = null;
        string? primaryCustomerName = null;
        if (delivery.PrimaryDeliveryId.HasValue)
        {
            // A sub-customer permit identifies the primary permit and the primary CUSTOMER by code and
            // name ONLY - no ledger, no charges, no contacts of theirs are carried onto this document.
            primaryNumber = await _db.Deliveries.AsNoTracking()
                .Where(d => d.Id == delivery.PrimaryDeliveryId.Value)
                .Select(d => d.DeliveryNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (delivery.PrimaryCustomerId.HasValue)
            {
                var primaryCustomer = await _db.Customers.AsNoTracking()
                    .Where(c => c.Id == delivery.PrimaryCustomerId.Value)
                    .Select(c => new { c.Code, c.Name })
                    .FirstOrDefaultAsync(cancellationToken);

                primaryCustomerCode = primaryCustomer?.Code;
                primaryCustomerName = primaryCustomer?.Name;
            }
        }

        var rows = lines.Select(line =>
        {
            orders.TryGetValue(line.ProductionOrderId, out var order);
            items.TryGetValue(line.ItemId, out var item);

            var stages = stageRows.Where(s => s.ProductionOrderId == line.ProductionOrderId).ToList();

            var originalKg = order?.RequestedQuantityKg;
            var originalMeter = order?.RequestedQuantityMeter;

            var producedKg = transfers.FirstOrDefault(t => t.ProductionOrderId == line.ProductionOrderId)?.QuantityKg
                             ?? lastStageOutput.FirstOrDefault(s => s.ProductionOrderId == line.ProductionOrderId)?.OutputKg;
            var producedMeter = transfers.FirstOrDefault(t => t.ProductionOrderId == line.ProductionOrderId)?.QuantityMeter
                                ?? lastStageOutput.FirstOrDefault(s => s.ProductionOrderId == line.ProductionOrderId)?.OutputMeter;

            var scrapsKg = Sum(stages.Select(s => s.SeparatesKg));
            var scrapsMeter = Sum(stages.Select(s => s.SeparatesMeter));
            var secondKg = Sum(stages.Select(s => s.SecondGradeKg));
            var secondMeter = Sum(stages.Select(s => s.SecondGradeMeter));

            return new DeliveryStatementRowDto
            {
                ProductionOrderId = line.ProductionOrderId,
                ProductionOrderNumber = order?.OrderNumber ?? string.Empty,
                ProductionOrderDate = order?.OrderDate,
                ItemId = line.ItemId,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.Name ?? string.Empty,
                Color = line.Color,
                OriginalRawQuantityKg = originalKg,
                OriginalRawQuantityMeter = originalMeter,
                DeliveredOriginalRawQuantityKg = line.OriginalRawQuantityKg,
                DeliveredOriginalRawQuantityMeter = line.OriginalRawQuantityMeter,
                RemainingOriginalRawKg = remaining.GetValueOrDefault(line.ProductionOrderId).Kg,
                RemainingOriginalRawMeter = remaining.GetValueOrDefault(line.ProductionOrderId).Meter,
                ReadyGoodsQuantityKg = line.QuantityKg,
                ReadyGoodsQuantityMeter = line.QuantityMeter,
                BaleCount = line.PieceCount,
                ScrapsKg = scrapsKg,
                ScrapsMeter = scrapsMeter,
                SecondGradeKg = secondKg,
                SecondGradeMeter = secondMeter,
                // Calculated waste, per unit and per unit only. Null (never a made-up number) when the
                // unit has no original-raw figure to measure against, and null when the recorded streams
                // would make it negative - that means the figures overlap or the original quantity is
                // understated, which is a data problem and not a waste figure to print on a permit.
                CalculatedWasteKg = Waste(originalKg, producedKg, scrapsKg, secondKg),
                CalculatedWasteMeter = Waste(originalMeter, producedMeter, scrapsMeter, secondMeter),
                DrawingNumbers = stages.Where(s => s.RequiresDrawingNumber && s.DrawingNumber != null)
                    .Select(s => s.DrawingNumber!).Distinct().ToList(),
                LineNotes = line.Notes
            };
        }).ToList();

        return new DeliveryStatementDto
        {
            DeliveryId = delivery.Id,
            DeliveryNumber = delivery.DeliveryNumber,
            DeliveryDate = delivery.DeliveryDate,
            Status = delivery.Status,
            Notes = delivery.Notes,
            CustomerId = delivery.CustomerId,
            CustomerCode = customer?.Code ?? string.Empty,
            CustomerName = customer?.Name ?? string.Empty,
            PrimaryDeliveryNumber = primaryNumber,
            PrimaryCustomerCode = primaryCustomerCode,
            PrimaryCustomerName = primaryCustomerName,
            Rows = rows
        };
    }

    /// <summary>Sum of a nullable stream, or null when the unit was never recorded anywhere.</summary>
    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var list = values.ToList();
        return list.Count == 0 || list.All(v => v is null) ? null : list.Sum(v => v ?? 0m);
    }

    /// <summary>
    /// original raw - ready goods - scraps/separates - second grade, within ONE unit. Null when the
    /// streams needed for the unit are missing, or when the difference would be negative (an
    /// impossible "waste" is not printed - see the class documentation).
    /// </summary>
    private static decimal? Waste(decimal? original, decimal? readyGoods, decimal? scraps, decimal? secondGrade)
    {
        if (original is null || readyGoods is null) return null;

        var waste = original.Value - readyGoods.Value - (scraps ?? 0m) - (secondGrade ?? 0m);
        return waste < 0m ? null : waste;
    }
}
