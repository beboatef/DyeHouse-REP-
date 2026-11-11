using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Negative stock overrides and period reopenings in the period (spec Part 1
/// metric 7): overrides from NegativeStockOverride (ApprovedAtUtc in range),
/// reopenings from the audit log (EntityName "PeriodClose", action "Update" in
/// range) joined back to the PeriodClose row for the period details. Reopening
/// a period is itself audited as an Update by the audit interceptor, so the
/// audit log is the authoritative reopening source.
/// </summary>
public record GetGovernanceKpiQuery(DateTime? From = null, DateTime? To = null)
    : IRequest<GovernanceKpiResult>, IKpiRange;

public class GetGovernanceKpiQueryValidator : KpiRangeValidator<GetGovernanceKpiQuery>;

public class GetGovernanceKpiQueryHandler : IRequestHandler<GetGovernanceKpiQuery, GovernanceKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetGovernanceKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<GovernanceKpiResult> Handle(GetGovernanceKpiQuery request, CancellationToken cancellationToken)
    {
        var overridesQuery = _db.NegativeStockOverrides.AsNoTracking().AsQueryable();
        if (request.From.HasValue) overridesQuery = overridesQuery.Where(o => o.ApprovedAtUtc >= request.From.Value);
        if (request.To.HasValue) overridesQuery = overridesQuery.Where(o => o.ApprovedAtUtc <= request.To.Value);
        var overrides = await overridesQuery.OrderBy(o => o.ApprovedAtUtc).ToListAsync(cancellationToken);

        var auditQuery = _db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityName == "PeriodClose" && a.Action == "Update");
        if (request.From.HasValue) auditQuery = auditQuery.Where(a => a.OccurredAtUtc >= request.From.Value);
        if (request.To.HasValue) auditQuery = auditQuery.Where(a => a.OccurredAtUtc <= request.To.Value);
        var reopenAudits = await auditQuery.OrderBy(a => a.OccurredAtUtc).ToListAsync(cancellationToken);

        var messageNumbers = overrides.Select(o => o.RawMessageId).Distinct().ToList();
        var messages = messageNumbers.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.RawMessages.AsNoTracking().Where(m => messageNumbers.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.MessageNumber, cancellationToken);
        var customerIds = overrides.Select(o => o.CustomerId).Distinct().ToList();
        var customers = customerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
        var itemIds = overrides.Select(o => o.ItemId).Distinct().ToList();
        var items = itemIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);

        var periodIds = reopenAudits
            .Select(a => Guid.TryParse(a.EntityId, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct().ToList();
        var periods = periodIds.Count == 0
            ? new Dictionary<Guid, Domain.Entities.PeriodClose>()
            : await _db.PeriodCloses.AsNoTracking().Where(p => periodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        var result = new GovernanceKpiResult
        {
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow
        };

        foreach (var o in overrides)
        {
            result.Overrides.Add(new NegativeStockOverrideKpiRow
            {
                Id = o.Id,
                ApprovedAtUtc = o.ApprovedAtUtc,
                MessageNumber = messages.GetValueOrDefault(o.RawMessageId, string.Empty),
                CustomerCode = customers.GetValueOrDefault(o.CustomerId, string.Empty),
                ItemCode = items.GetValueOrDefault(o.ItemId, string.Empty),
                RequestedQuantity = o.RequestedQuantity,
                Reason = o.Reason,
                ApprovedBy = o.ApprovedBy
            });
        }

        foreach (var a in reopenAudits)
        {
            if (!Guid.TryParse(a.EntityId, out var periodId) || !periods.TryGetValue(periodId, out var period))
                continue;
            result.Reopenings.Add(new PeriodReopeningKpiRow
            {
                PeriodCloseId = period.Id,
                PeriodStart = period.PeriodStart,
                PeriodEnd = period.PeriodEnd,
                ReopenedAtUtc = period.ReopenedAtUtc ?? a.OccurredAtUtc,
                ReopenedBy = period.ReopenedBy,
                AuditOccurredAtUtc = a.OccurredAtUtc,
                AuditUserName = a.UserName
            });
        }

        result.OverridesCount = result.Overrides.Count;
        result.ReopeningsCount = result.Reopenings.Count;
        return result;
    }
}
