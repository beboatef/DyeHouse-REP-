using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.Commands;
using DyeHouseERP.Application.ProductionStages.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Queries;

/// <summary>
/// Lists the configured production stages. <paramref name="ActiveOnly"/> is the state
/// filter (deactivating a stage only ever removes it from the ACTIVE list - the row and
/// its history stay, so an administrator can always see and restore it), and
/// <paramref name="Search"/> is an optional text filter over code and name.
/// </summary>
public record GetProductionStageDefinitionsQuery(bool? ActiveOnly = null, string? Search = null)
    : IRequest<List<ProductionStageDefinitionDto>>;

public class GetProductionStageDefinitionsQueryHandler
    : IRequestHandler<GetProductionStageDefinitionsQuery, List<ProductionStageDefinitionDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionStageDefinitionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionStageDefinitionDto>> Handle(GetProductionStageDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionStageDefinitions.AsNoTracking().AsQueryable();
        if (request.ActiveOnly == true) query = query.Where(s => s.IsActive);

        // Filtered in the database, not in the client: an installation with many stages
        // should never have to ship them all just to search for one.
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = search.ToLower();
            query = query.Where(s => s.Code.ToLower().Contains(pattern) || s.Name.ToLower().Contains(pattern));
        }

        var stages = await query.OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        return stages.Select(CreateProductionStageDefinitionCommandHandler.Map).ToList();
    }
}
