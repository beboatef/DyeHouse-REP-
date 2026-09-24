using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Queries;

public record GetItemsQuery(bool? ActiveOnly = null, string? Search = null) : IRequest<List<ItemDto>>;

public class GetItemsQueryHandler : IRequestHandler<GetItemsQuery, List<ItemDto>>
{
    private readonly IApplicationDbContext _db;
    public GetItemsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ItemDto>> Handle(GetItemsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Items.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(i => i.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(i => i.Code.Contains(request.Search) || i.Name.Contains(request.Search));

        return await query
            .OrderBy(i => i.Code)
            .Select(i => new ItemDto { Id = i.Id, Code = i.Code, Name = i.Name, BaseUnit = i.BaseUnit, IsActive = i.IsActive })
            .ToListAsync(cancellationToken);
    }
}
