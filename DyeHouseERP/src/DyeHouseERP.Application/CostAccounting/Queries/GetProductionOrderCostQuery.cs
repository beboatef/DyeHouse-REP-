using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CostAccounting.Queries;

/// <summary>
/// Total Production Order cost rollup (spec section 35):
/// Material + Preparation + allocated Operating Cost (labor/electricity/
/// fuel/maintenance/other) = Total Cost; cost per KG/Meter and margin are
/// only computed when their denominator is actually positive - never a
/// divide-by-zero.
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

        var costEntries = await _db.CostEntries.AsNoTracking()
            .Where(c => c.ProductionOrderId == request.ProductionOrderId)
            .ToListAsync(cancellationToken);

        decimal Sum(CostCategory category) => costEntries.Where(c => c.Category == category).Sum(c => c.Amount);
        var labor = Sum(CostCategory.Labor);
        var electricity = Sum(CostCategory.Electricity);
        var fuel = Sum(CostCategory.Fuel);
        var maintenance = Sum(CostCategory.Maintenance);
        var other = Sum(CostCategory.Other);

        var totalCost = materialCost + preparationCost + labor + electricity + fuel + maintenance + other;

        // Output quantity = what was actually transferred to ready goods for this order.
        var readyTransfer = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => t.ProductionOrderId == request.ProductionOrderId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter })
            .FirstOrDefaultAsync(cancellationToken);

        var outputKg = readyTransfer?.QuantityKg;
        var outputMeter = readyTransfer?.QuantityMeter;

        var processingValue = await _db.InvoiceLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == request.ProductionOrderId)
            .SumAsync(l => (decimal?)(l.Quantity * l.ProcessingPrice), cancellationToken) ?? 0;

        var profit = processingValue - totalCost;

        return new ProductionOrderCostDto
        {
            ProductionOrderId = order.Id, ProductionOrderNumber = order.OrderNumber,
            MaterialCost = materialCost, PreparationCost = preparationCost,
            LaborCost = labor, ElectricityCost = electricity, FuelCost = fuel, MaintenanceCost = maintenance, OtherCost = other,
            TotalCost = totalCost,
            OutputKg = outputKg, OutputMeter = outputMeter,
            CostPerKg = outputKg is > 0 ? totalCost / outputKg : null,
            CostPerMeter = outputMeter is > 0 ? totalCost / outputMeter : null,
            ProcessingValue = processingValue, Profit = profit,
            MarginPercent = processingValue > 0 ? Math.Round(profit / processingValue * 100, 2) : null
        };
    }
}
