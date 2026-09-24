using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Queries;

public record GetMaterialPreparationsQuery : IRequest<List<MaterialPreparationDto>>;

public class GetMaterialPreparationsQueryHandler : IRequestHandler<GetMaterialPreparationsQuery, List<MaterialPreparationDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialPreparationsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialPreparationDto>> Handle(GetMaterialPreparationsQuery request, CancellationToken cancellationToken)
    {
        var preparations = await _db.MaterialPreparations.AsNoTracking().OrderByDescending(p => p.PreparationDate).ToListAsync(cancellationToken);
        if (preparations.Count == 0) return new List<MaterialPreparationDto>();

        var materials = await _db.Materials.AsNoTracking().Where(m => preparations.Select(p => p.OriginalMaterialId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        return preparations.Select(p => new MaterialPreparationDto
        {
            Id = p.Id, PreparationNumber = p.PreparationNumber, PreparationDate = p.PreparationDate,
            OriginalMaterialId = p.OriginalMaterialId, OriginalMaterialCode = materials.GetValueOrDefault(p.OriginalMaterialId)?.Code ?? "",
            OriginalQuantity = p.OriginalQuantity, WaterQuantity = p.WaterQuantity, ResultingQuantity = p.ResultingQuantity,
            Concentration = p.Concentration, Cost = p.Cost, ProductionOrderId = p.ProductionOrderId, Notes = p.Notes
        }).ToList();
    }
}
