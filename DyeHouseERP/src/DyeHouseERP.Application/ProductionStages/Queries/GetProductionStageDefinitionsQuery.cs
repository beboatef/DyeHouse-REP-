using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.Commands;
using DyeHouseERP.Application.ProductionStages.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Queries;

public record GetProductionStageDefinitionsQuery(bool? ActiveOnly = null) : IRequest<List<ProductionStageDefinitionDto>>;

public class GetProductionStageDefinitionsQueryHandler
    : IRequestHandler<GetProductionStageDefinitionsQuery, List<ProductionStageDefinitionDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionStageDefinitionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionStageDefinitionDto>> Handle(GetProductionStageDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionStageDefinitions.AsNoTracking().AsQueryable();
        if (request.ActiveOnly == true) query = query.Where(s => s.IsActive);

        var stages = await query.OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        return stages.Select(CreateProductionStageDefinitionCommandHandler.Map).ToList();
    }
}
