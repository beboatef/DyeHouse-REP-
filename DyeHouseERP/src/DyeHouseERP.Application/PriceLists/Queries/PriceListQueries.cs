using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CostAccounting.Queries;
using DyeHouseERP.Application.PriceLists.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PriceLists.Queries;

// =====================================================================
// Actual Cost List
// =====================================================================

public record GetStageCostRatesQuery(Guid? StageDefinitionId = null, bool? ActiveOnly = null)
    : IRequest<List<StageCostRateDto>>;

public class GetStageCostRatesQueryHandler : IRequestHandler<GetStageCostRatesQuery, List<StageCostRateDto>>
{
    private readonly IApplicationDbContext _db;
    public GetStageCostRatesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<StageCostRateDto>> Handle(GetStageCostRatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.StageCostRates.AsNoTracking().AsQueryable();
        if (request.StageDefinitionId.HasValue) query = query.Where(r => r.StageDefinitionId == request.StageDefinitionId.Value);
        if (request.ActiveOnly == true) query = query.Where(r => r.IsActive);

        var rates = await query.ToListAsync(cancellationToken);
        var stages = await _db.ProductionStageDefinitions.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return rates
            .OrderBy(r => stages.TryGetValue(r.StageDefinitionId, out var s) ? s.Sequence : int.MaxValue)
            .ThenBy(r => r.Unit)
            .Select(r => PriceListMapping.ToDto(r, stages.TryGetValue(r.StageDefinitionId, out var s) ? s : null))
            .ToList();
    }
}

// =====================================================================
// Customer Service Price List
// =====================================================================

/// <summary>Lists service prices. <paramref name="GeneralOnly"/> isolates the default rows; <paramref name="CustomerId"/> shows that customer's own rows.</summary>
public record GetCustomerServicePricesQuery(
    Guid? CustomerId = null, bool? ActiveOnly = null, Guid? StageDefinitionId = null, bool? GeneralOnly = null)
    : IRequest<List<CustomerServicePriceDto>>;

public class GetCustomerServicePricesQueryHandler
    : IRequestHandler<GetCustomerServicePricesQuery, List<CustomerServicePriceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerServicePricesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CustomerServicePriceDto>> Handle(
        GetCustomerServicePricesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.CustomerServicePrices.AsNoTracking().AsQueryable();

        if (request.GeneralOnly == true) query = query.Where(p => p.CustomerId == null);
        else if (request.CustomerId.HasValue) query = query.Where(p => p.CustomerId == request.CustomerId.Value);

        if (request.StageDefinitionId.HasValue) query = query.Where(p => p.StageDefinitionId == request.StageDefinitionId.Value);
        if (request.ActiveOnly == true) query = query.Where(p => p.IsActive);

        var prices = await query.ToListAsync(cancellationToken);
        var stages = await _db.ProductionStageDefinitions.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);

        return prices
            .OrderBy(p => stages.TryGetValue(p.StageDefinitionId, out var s) ? s.Sequence : int.MaxValue)
            .ThenBy(p => p.CustomerId is null ? 0 : 1)
            .Select(p => PriceListMapping.ToDto(
                p,
                stages.TryGetValue(p.StageDefinitionId, out var s) ? s : null,
                p.CustomerId.HasValue && customers.TryGetValue(p.CustomerId.Value, out var c) ? c : null))
            .ToList();
    }
}

// =====================================================================
// Job Order profitability (spec section 37)
// =====================================================================

public record GetProductionOrderProfitabilityQuery(Guid ProductionOrderId) : IRequest<ProductionOrderProfitabilityDto>;

public class GetProductionOrderProfitabilityQueryHandler
    : IRequestHandler<GetProductionOrderProfitabilityQuery, ProductionOrderProfitabilityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;

    public GetProductionOrderProfitabilityQueryHandler(IApplicationDbContext db, ISender mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<ProductionOrderProfitabilityDto> Handle(
        GetProductionOrderProfitabilityQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var snapshots = await _db.ProductionOrderServicePrices.AsNoTracking()
            .Where(s => s.ProductionOrderId == request.ProductionOrderId)
            .ToListAsync(cancellationToken);

        var stages = await _db.ProductionStageDefinitions.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var customers = await _db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);

        var (finalKg, finalMeter, source) = await ResolveFinalQuantityAsync(request.ProductionOrderId, cancellationToken);

        // Revenue is computed ONCE for the order: final actual quantity x the
        // snapshotted price for that unit - never summed per stage.
        decimal revenue = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Unit == UnitOfMeasure.KG) revenue += finalKg * snapshot.PricePerUnit;
            else if (snapshot.Unit == UnitOfMeasure.Meter) revenue += finalMeter * snapshot.PricePerUnit;
        }

        // Cost comes from the existing live rollup, so there is exactly one
        // implementation of "what did this job actually cost".
        var cost = await _mediator.Send(new GetProductionOrderCostQuery(request.ProductionOrderId), cancellationToken);

        var profit = revenue - cost.TotalCost;

        return new ProductionOrderProfitabilityDto
        {
            ProductionOrderId = order.Id,
            ProductionOrderNumber = order.OrderNumber,
            ServicePrices = snapshots
                .OrderBy(s => s.Unit)
                .Select(s => PriceListMapping.ToDto(
                    s,
                    stages.TryGetValue(s.StageDefinitionId, out var st) ? st : null,
                    s.SourceCustomerId.HasValue && customers.TryGetValue(s.SourceCustomerId.Value, out var c) ? c : null))
                .ToList(),
            FinalQuantityKg = finalKg,
            FinalQuantityMeter = finalMeter,
            FinalQuantitySource = source,
            Revenue = revenue,
            HasServicePrice = snapshots.Count > 0,
            ActualCost = cost.TotalCost,
            Profit = profit,
            MarginPercent = revenue > 0 ? Math.Round(profit / revenue * 100, 2) : null,
            LegacyProcessingValue = cost.ProcessingValue
        };
    }

    /// <summary>
    /// Final actual quantity (spec section 35): what was transferred to ready goods,
    /// falling back to the last completed stage's output so a job in production still
    /// reports a usable figure. The source is returned so the UI never has to guess
    /// which of the two it is looking at.
    /// </summary>
    private async Task<(decimal Kg, decimal Meter, string Source)> ResolveFinalQuantityAsync(
        Guid productionOrderId, CancellationToken cancellationToken)
    {
        var transfer = await _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => t.ProductionOrderId == productionOrderId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter })
            .FirstOrDefaultAsync(cancellationToken);

        if (transfer is not null)
            return (transfer.QuantityKg ?? 0, transfer.QuantityMeter ?? 0, "ReadyGoodsTransfer");

        var lastStage = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => s.ProductionOrderId == productionOrderId && s.Status == StageExecutionStatus.Completed)
            .OrderByDescending(s => s.Sequence)
            .Select(s => new { s.OutputKg, s.OutputMeter })
            .FirstOrDefaultAsync(cancellationToken);

        if (lastStage is not null)
            return (lastStage.OutputKg ?? 0, lastStage.OutputMeter ?? 0, "LastStageOutput");

        return (0, 0, "None");
    }
}
