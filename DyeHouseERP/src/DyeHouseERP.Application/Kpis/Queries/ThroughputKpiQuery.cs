using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Throughput per stage and per shift (spec Part 1 metric 4): quantity moved
/// (QuantityKg) by posted ready-goods transfers in the period, grouped by the
/// stage that produced the lot (via the transfer's StageExecutionId link) and
/// by the shift that ran that execution. Transfers whose historical execution
/// link is null group under "?" - the link is never fabricated.
/// </summary>
public record GetThroughputKpiQuery(DateTime? From = null, DateTime? To = null)
    : IRequest<ThroughputKpiResult>, IKpiRange;

public class GetThroughputKpiQueryValidator : KpiRangeValidator<GetThroughputKpiQuery>;

public class GetThroughputKpiQueryHandler : IRequestHandler<GetThroughputKpiQuery, ThroughputKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetThroughputKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ThroughputKpiResult> Handle(GetThroughputKpiQuery request, CancellationToken cancellationToken)
    {
        var transfersQuery = _db.ReadyGoodsTransfers.AsNoTracking()
            .Where(t => t.Status == ReadyGoodsTransferStatus.Posted);
        if (request.From.HasValue) transfersQuery = transfersQuery.Where(t => t.TransferDate >= request.From.Value);
        if (request.To.HasValue) transfersQuery = transfersQuery.Where(t => t.TransferDate <= request.To.Value);

        var transfers = await transfersQuery
            .Select(t => new { t.StageExecutionId, QuantityKg = t.QuantityKg ?? 0m })
            .ToListAsync(cancellationToken);

        var executionIds = transfers.Where(t => t.StageExecutionId.HasValue).Select(t => t.StageExecutionId!.Value).Distinct().ToList();
        var executions = executionIds.Count == 0
            ? new Dictionary<Guid, (Guid StageDefinitionId, Guid? ShiftId)>()
            : await _db.ProductionOrderStageExecutions.AsNoTracking()
                .Where(e => executionIds.Contains(e.Id))
                .Select(e => new { e.Id, e.StageDefinitionId, e.ProductionShiftId })
                .ToDictionaryAsync(e => e.Id, e => (StageDefinitionId: e.StageDefinitionId, ShiftId: e.ProductionShiftId), cancellationToken);

        var stageDefs = await _db.ProductionStageDefinitions.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => new { s.Code, s.Name }, cancellationToken);
        var shifts = await _db.ProductionShifts.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => new { s.Code, s.Name }, cancellationToken);

        var result = new ThroughputKpiResult
        {
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow
        };

        var byStage = transfers
            .GroupBy(t => t.StageExecutionId.HasValue && executions.TryGetValue(t.StageExecutionId.Value, out var ex)
                ? ex.StageDefinitionId : (Guid?)null)
            .Select(g =>
            {
                stageDefs.TryGetValue(g.Key ?? Guid.Empty, out var def);
                return new ThroughputGroupRow
                {
                    Code = def?.Code ?? "?",
                    Name = def?.Name ?? "?",
                    TransfersCount = g.Count(),
                    QuantityKg = g.Sum(t => t.QuantityKg)
                };
            })
            .OrderByDescending(r => r.QuantityKg);
        result.ByStage.AddRange(byStage);

        var byShift = transfers
            .GroupBy(t => t.StageExecutionId.HasValue && executions.TryGetValue(t.StageExecutionId.Value, out var ex)
                ? ex.ShiftId : null)
            .Select(g =>
            {
                shifts.TryGetValue(g.Key ?? Guid.Empty, out var shift);
                return new ThroughputGroupRow
                {
                    Code = shift?.Code ?? "?",
                    Name = shift?.Name ?? "?",
                    TransfersCount = g.Count(),
                    QuantityKg = g.Sum(t => t.QuantityKg)
                };
            })
            .OrderByDescending(r => r.QuantityKg);
        result.ByShift.AddRange(byShift);

        result.TotalTransfers = transfers.Count;
        result.TotalQuantityKg = transfers.Sum(t => t.QuantityKg);
        return result;
    }
}
