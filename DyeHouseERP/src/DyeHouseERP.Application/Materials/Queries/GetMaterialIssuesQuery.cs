using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Queries;

public record GetMaterialIssuesQuery(Guid? ProductionOrderId = null) : IRequest<List<MaterialIssueDto>>;

public class GetMaterialIssuesQueryHandler : IRequestHandler<GetMaterialIssuesQuery, List<MaterialIssueDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialIssuesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialIssueDto>> Handle(GetMaterialIssuesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.MaterialIssues.AsNoTracking().AsQueryable();
        if (request.ProductionOrderId.HasValue) query = query.Where(i => i.ProductionOrderId == request.ProductionOrderId);

        // Cancelled rows are kept in the list (with IsCancelled set) so history stays visible in the UI.
        var issues = await query.OrderByDescending(i => i.IssueDate).ToListAsync(cancellationToken);
        return await BuildManyAsync(_db, issues, cancellationToken);
    }

    /// <summary>Shared mapping used by the list query and by write commands that return the saved issue.</summary>
    public static async Task<List<MaterialIssueDto>> BuildManyAsync(
        IApplicationDbContext db, IReadOnlyCollection<Domain.Entities.MaterialIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return new List<MaterialIssueDto>();

        var materialIds = issues.Select(i => i.MaterialId).Distinct().ToList();
        var orderIds = issues.Select(i => i.ProductionOrderId).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);

        return issues.Select(i => new MaterialIssueDto
        {
            Id = i.Id, IssueNumber = i.IssueNumber, IssueDate = i.IssueDate,
            MaterialId = i.MaterialId, MaterialCode = materials.GetValueOrDefault(i.MaterialId)?.Code ?? "", MaterialName = materials.GetValueOrDefault(i.MaterialId)?.Name ?? "",
            WarehouseId = i.WarehouseId, ProductionOrderId = i.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(i.ProductionOrderId)?.OrderNumber ?? "",
            Quantity = i.Quantity, UnitCost = i.UnitCost, TotalCost = i.TotalCost, Notes = i.Notes,
            Status = i.Status, IsCancelled = i.IsCancelled,
            CancelledBy = i.CancelledBy, CancelledAtUtc = i.CancelledAtUtc, CancellationReason = i.CancellationReason
        }).ToList();
    }
}
