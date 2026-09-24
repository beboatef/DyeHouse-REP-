using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Queries;

public record GetMaterialsQuery(bool? ActiveOnly = null) : IRequest<List<MaterialDto>>;

public class GetMaterialsQueryHandler : IRequestHandler<GetMaterialsQuery, List<MaterialDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialDto>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Materials.AsNoTracking().AsQueryable();
        if (request.ActiveOnly == true) query = query.Where(m => m.IsActive);

        return await query.OrderBy(m => m.Code)
            .Select(m => new MaterialDto { Id = m.Id, Code = m.Code, Name = m.Name, Unit = m.Unit, PurchasePrice = m.PurchasePrice, IsActive = m.IsActive })
            .ToListAsync(cancellationToken);
    }
}
