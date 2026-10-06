using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CostAccounting.Queries;

/// <summary>
/// Total Job Order cost rollup (spec section 34):
/// Material + Preparation + External Processing + allocated Operating Cost
/// (labor/electricity/fuel/maintenance/other) = ACTUAL cost, always derived
/// live from posted transactions so it can never drift from them. The
/// ESTIMATED and APPROVED figures are stored on the order and returned
/// alongside it; cost per KG/Meter and margin are only computed when their
/// denominator is actually positive - never a divide-by-zero.
/// Customer-owned raw material is reported as quantities only and is NEVER
/// valued into this cost. Only factory-owned material issues are costed.
/// </summary>
public record GetProductionOrderCostQuery(Guid ProductionOrderId) : IRequest<ProductionOrderCostDto>;

public class GetProductionOrderCostQueryHandler : IRequestHandler<GetProductionOrderCostQuery, ProductionOrderCostDto>
{
    private readonly IApplicationDbContext _db;
    public GetProductionOrderCostQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductionOrderCostDto> Handle(GetProductionOrderCostQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var materialCost = await _db.MaterialIssues.AsNoTracking()
            .Where(i => i.ProductionOrderId == request.ProductionOrderId)
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitCost), cancellationToken) ?? 0;

        var preparationCost = await _db.MaterialPreparations.AsNoTracking()
            .Where(p => p.ProductionOrderId == request.ProductionOrderId)
            .SumAsync(p => p.Cost, cancellationToken) ?? 0;

        // External processing (spec section 21): the cost charged by the outside
        // processor, linked to this order at the time the material was released.
        var externalProcessingCost = await _db.RawExternalReleases.AsNoTracking()
            .Where(r => r.ProductionOrderId == request.ProductionOrderId &&
                        r.Reason == RawReleaseReason.ExternalProcessing)
            .SumAsync(r => (decimal?)r.ExternalProcessingCost, cancellationToken) ?? 0;

        var costEntries = await _db.CostEntries.AsNoTracking()
            .Where(c => c.ProductionOrderId == request.ProductionOrderId)
            .ToListAsync(cancellationToken);

        decimal Sum(CostCategory category) => costEntries.Where(c => c.Category == category).Sum(c => c.Amount);
        var labor = Sum(CostCategory.Labor);
        var electricity = Sum(CostCategory.Electricity);
        var fuel = Sum(CostCategory.Fuel);
        var maintenance = Sum(CostCategory.Maintenance);

        // Additional cost lines (spec section 35). External processing is NOT summed
        // from here - it is derived from the release rows above, so counting a
        // hand-entered entry too would double the same charge.
        var transport = Sum(CostCategory.Transport);
        var packaging = Sum(CostCategory.Packaging);
        var repair = Sum(CostCategory.Repair);
        var other = Sum(CostCategory.Other);

        var totalCost = materialCost + preparationCost + externalProcessingCost
                        + labor + electricity + fuel + maintenance
                        + transport + packaging + repair + other;

        // Final actual quantity (spec section 17). NOT the planned/requested figure:
        // what was transferred to Ready Goods is authoritative, and while the order
        // is still running the last COMPLETED stage's output is the closest real
        // figure available - the same resolution the profitability screen uses, so
        // the two screens can never quote different denominators for one order.
        var readyTransfer = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => t.ProductionOrderId == request.ProductionOrderId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter })
            .FirstOrDefaultAsync(cancellationToken);

        decimal? outputKg = readyTransfer?.QuantityKg;
        decimal? outputMeter = readyTransfer?.QuantityMeter;

        if (outputKg is null && outputMeter is null)
        {
            var lastStage = await _db.ProductionOrderStageExecutions.AsNoTracking()
                .Where(s => s.ProductionOrderId == request.ProductionOrderId
                         && s.Status == StageExecutionStatus.Completed)
                .OrderByDescending(s => s.Sequence)
                .Select(s => new { s.OutputKg, s.OutputMeter })
                .FirstOrDefaultAsync(cancellationToken);

            outputKg = lastStage?.OutputKg;
            outputMeter = lastStage?.OutputMeter;
        }

        var processingValue = await _db.InvoiceLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == request.ProductionOrderId)
            .SumAsync(l => (decimal?)(l.Quantity * l.ProcessingPrice), cancellationToken) ?? 0;

        var profit = processingValue - totalCost;

        return new ProductionOrderCostDto
        {
            ProductionOrderId = order.Id, ProductionOrderNumber = order.OrderNumber,
            MaterialCost = materialCost, PreparationCost = preparationCost,
            ExternalProcessingCost = externalProcessingCost,
            LaborCost = labor, ElectricityCost = electricity, FuelCost = fuel, MaintenanceCost = maintenance,
            TransportCost = transport, PackagingCost = packaging, RepairCost = repair, OtherCost = other,
            TotalCost = totalCost,
            EstimatedCost = order.EstimatedCost,
            ApprovedCost = order.ApprovedCost,
            CostApprovedBy = order.CostApprovedBy,
            CostApprovedAtUtc = order.CostApprovedAtUtc,
            CostingNotes = order.CostingNotes,
            ApprovedVariance = order.ApprovedCost is null ? null : order.ApprovedCost.Value - totalCost,
            OutputKg = outputKg, OutputMeter = outputMeter,
            // Null = N/A when the final actual quantity is zero or not yet known;
            // never a division by zero and never a made-up figure.
            CostPerKg = outputKg is > 0 ? totalCost / outputKg : null,
            CostPerMeter = outputMeter is > 0 ? totalCost / outputMeter : null,
            ProcessingValue = processingValue, Profit = profit,
            MarginPercent = processingValue > 0 ? Math.Round(profit / processingValue * 100, 2) : null
        };
    }
}
