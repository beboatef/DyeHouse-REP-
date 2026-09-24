using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Separates.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Queries;

public record GetSeparatesQuery(SeparateStatus? Status = null) : IRequest<List<SeparateDto>>;

public class GetSeparatesQueryHandler : IRequestHandler<GetSeparatesQuery, List<SeparateDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSeparatesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SeparateDto>> Handle(GetSeparatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Separates.AsNoTracking().AsQueryable();
        if (request.Status.HasValue) query = query.Where(s => s.Status == request.Status);

        var separates = await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        if (separates.Count == 0) return new List<SeparateDto>();

        var orderIds = separates.Select(s => s.OriginalProductionOrderId)
            .Concat(separates.Where(s => s.ReprocessingProductionOrderId.HasValue).Select(s => s.ReprocessingProductionOrderId!.Value))
            .Distinct().ToList();
        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        var stageExecIds = separates.Select(s => s.StageExecutionId).Distinct().ToList();
        var stageExecs = await _db.ProductionOrderStageExecutions.AsNoTracking().Where(s => stageExecIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var stageDefIds = stageExecs.Values.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageDefs = await _db.ProductionStageDefinitions.AsNoTracking().Where(d => stageDefIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);

        var customers = await _db.Customers.AsNoTracking().Where(c => separates.Select(s => s.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => separates.Select(s => s.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        return separates.Select(s =>
        {
            stageExecs.TryGetValue(s.StageExecutionId, out var stageExec);
            var stageName = stageExec != null && stageDefs.TryGetValue(stageExec.StageDefinitionId, out var def) ? def.Name : string.Empty;

            return new SeparateDto
            {
                Id = s.Id,
                OriginalProductionOrderId = s.OriginalProductionOrderId,
                OriginalOrderNumber = orders.GetValueOrDefault(s.OriginalProductionOrderId)?.OrderNumber ?? string.Empty,
                StageExecutionId = s.StageExecutionId,
                StageName = stageName,
                CustomerId = s.CustomerId,
                CustomerCode = customers.GetValueOrDefault(s.CustomerId)?.Code ?? string.Empty,
                ItemId = s.ItemId,
                ItemCode = items.GetValueOrDefault(s.ItemId)?.Code ?? string.Empty,
                ItemName = items.GetValueOrDefault(s.ItemId)?.Name ?? string.Empty,
                QuantityKg = s.QuantityKg,
                QuantityMeter = s.QuantityMeter,
                Reason = s.Reason,
                Notes = s.Notes,
                Status = s.Status,
                ReprocessingProductionOrderId = s.ReprocessingProductionOrderId,
                ReprocessingOrderNumber = s.ReprocessingProductionOrderId.HasValue
                    ? orders.GetValueOrDefault(s.ReprocessingProductionOrderId.Value)?.OrderNumber
                    : null
            };
        }).ToList();
    }
}
