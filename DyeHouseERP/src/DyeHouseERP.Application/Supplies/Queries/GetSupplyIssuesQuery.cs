using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Supplies.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Supplies.Queries;

/// <summary>Operating supplies issue list (spec section 27).</summary>
public record GetSupplyIssuesQuery(SupplyIssueStatus? Status = null, Guid? WarehouseId = null,
    string? Search = null, DateTime? From = null, DateTime? To = null) : IRequest<List<SupplyIssueDto>>;

public class GetSupplyIssuesQueryHandler : IRequestHandler<GetSupplyIssuesQuery, List<SupplyIssueDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSupplyIssuesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplyIssueDto>> Handle(GetSupplyIssuesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.SupplyIssues.AsNoTracking().Include(i => i.Lines).AsQueryable();

        if (request.Status is not null) query = query.Where(i => i.Status == request.Status);
        if (request.WarehouseId is not null) query = query.Where(i => i.WarehouseId == request.WarehouseId);
        if (request.From is not null) query = query.Where(i => i.IssueDate >= request.From.Value);
        if (request.To is not null) query = query.Where(i => i.IssueDate <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(i => i.IssueNumber.Contains(term) || i.IssuedTo.Contains(term) || i.Purpose.Contains(term));
        }

        var issues = await query.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.IssueNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await SupplyIssueDtoBuilder.BuildManyAsync(_db, issues, cancellationToken);
    }
}

public record GetSupplyIssueByIdQuery(Guid Id) : IRequest<SupplyIssueDto>;

public class GetSupplyIssueByIdQueryHandler : IRequestHandler<GetSupplyIssueByIdQuery, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    public GetSupplyIssueByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<SupplyIssueDto> Handle(GetSupplyIssueByIdQuery request, CancellationToken cancellationToken)
    {
        var issue = await _db.SupplyIssues.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplyIssue", request.Id);

        var built = await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken);
        var dto = built[0];

        var entityId = issue.Id.ToString();
        var audit = await _db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        dto.Timeline = audit.Select(a => new SupplyIssueTimelineDto
        {
            Action = a.Action, User = a.UserName, AtUtc = a.OccurredAtUtc, Detail = a.AfterDataJson
        }).ToList();

        return dto;
    }
}

/// <summary>
/// Shared mapper so the list and the single-record views can never disagree
/// about balances or material names.
/// </summary>
internal static class SupplyIssueDtoBuilder
{
    public static async Task<List<SupplyIssueDto>> BuildManyAsync(
        IApplicationDbContext db, IReadOnlyCollection<Domain.Entities.SupplyIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return new List<SupplyIssueDto>();

        var warehouseIds = issues.Select(i => i.WarehouseId).Distinct().ToList();
        var departmentIds = issues.Where(i => i.DepartmentId != null).Select(i => i.DepartmentId!.Value).Distinct().ToList();
        var materialIds = issues.SelectMany(i => i.Lines).Select(l => l.MaterialId).Distinct().ToList();

        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var departments = await db.Departments.AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var materials = await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m, ct);

        var result = new List<SupplyIssueDto>(issues.Count);

        foreach (var issue in issues)
        {
            var dto = new SupplyIssueDto
            {
                Id = issue.Id, IssueNumber = issue.IssueNumber, IssueDate = issue.IssueDate,
                WarehouseId = issue.WarehouseId,
                WarehouseName = warehouses.TryGetValue(issue.WarehouseId, out var wn) ? wn : string.Empty,
                DepartmentId = issue.DepartmentId,
                DepartmentName = issue.DepartmentId is not null && departments.TryGetValue(issue.DepartmentId.Value, out var dn) ? dn : null,
                IssuedTo = issue.IssuedTo, Purpose = issue.Purpose, Notes = issue.Notes,
                Status = issue.Status, TotalCost = issue.TotalCost, IsEditable = issue.IsEditable,
                PostedBy = issue.PostedBy, PostedAtUtc = issue.PostedAtUtc,
                CancelledBy = issue.CancelledBy, CancelledAtUtc = issue.CancelledAtUtc,
                CancellationReason = issue.CancellationReason,
                CreatedBy = issue.CreatedBy, CreatedAtUtc = issue.CreatedAtUtc,
                ModifiedBy = issue.ModifiedBy, ModifiedAtUtc = issue.ModifiedAtUtc
            };

            foreach (var line in issue.Lines)
            {
                materials.TryGetValue(line.MaterialId, out var material);
                dto.Lines.Add(new SupplyIssueLineDto
                {
                    Id = line.Id, MaterialId = line.MaterialId,
                    MaterialCode = material?.Code ?? string.Empty,
                    MaterialName = material?.Name ?? string.Empty,
                    Quantity = line.Quantity, Unit = line.Unit,
                    UnitCost = line.UnitCost, TotalCost = line.TotalCost, Notes = line.Notes
                });
            }

            result.Add(dto);
        }

        return result;
    }
}
