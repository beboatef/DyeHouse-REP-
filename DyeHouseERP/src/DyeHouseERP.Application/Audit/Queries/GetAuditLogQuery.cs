using DyeHouseERP.Application.Audit.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Audit.Queries;

/// <summary>Audit reports (spec section 35 "Audit reports", section 42) - filterable by entity, user, and date.</summary>
public record GetAuditLogQuery(
    string? EntityName = null, string? UserName = null, DateTime? From = null, DateTime? To = null, int Take = 200)
    : IRequest<List<AuditLogEntryDto>>;

public class GetAuditLogQueryHandler : IRequestHandler<GetAuditLogQuery, List<AuditLogEntryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetAuditLogQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AuditLogEntryDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        var query = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.EntityName)) query = query.Where(a => a.EntityName == request.EntityName);
        if (!string.IsNullOrWhiteSpace(request.UserName)) query = query.Where(a => a.UserName == request.UserName);
        if (request.From.HasValue) query = query.Where(a => a.OccurredAtUtc >= request.From);
        if (request.To.HasValue) query = query.Where(a => a.OccurredAtUtc <= request.To);

        return await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(Math.Clamp(request.Take, 1, 1000))
            .Select(a => new AuditLogEntryDto
            {
                Id = a.Id, OccurredAtUtc = a.OccurredAtUtc, UserName = a.UserName, Action = a.Action,
                EntityName = a.EntityName, EntityId = a.EntityId, BeforeDataJson = a.BeforeDataJson,
                AfterDataJson = a.AfterDataJson, IpAddress = a.IpAddress, Reason = a.Reason
            })
            .ToListAsync(cancellationToken);
    }
}
