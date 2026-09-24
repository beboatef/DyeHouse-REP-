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

        var issues = await query.OrderByDescending(i => i.IssueDate).ToListAsync(cancellationToken);
        if (issues.Count == 0) return new List<MaterialIssueDto>();

        var materials = await _db.Materials.AsNoTracking().Where(m => issues.Select(i => i.MaterialId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => issues.Select(i => i.ProductionOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        return issues.Select(i => new MaterialIssueDto
        {
            Id = i.Id, IssueNumber = i.IssueNumber, IssueDate = i.IssueDate,
            MaterialId = i.MaterialId, MaterialCode = materials.GetValueOrDefault(i.MaterialId)?.Code ?? "", MaterialName = materials.GetValueOrDefault(i.MaterialId)?.Name ?? "",
            WarehouseId = i.WarehouseId, ProductionOrderId = i.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(i.ProductionOrderId)?.OrderNumber ?? "",
            Quantity = i.Quantity, UnitCost = i.UnitCost, TotalCost = i.TotalCost, Notes = i.Notes
        }).ToList();
    }
}
