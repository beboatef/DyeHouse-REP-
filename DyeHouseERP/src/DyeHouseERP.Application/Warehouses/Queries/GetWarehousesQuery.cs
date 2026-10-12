using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Warehouses.Commands;
using DyeHouseERP.Application.Warehouses.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Warehouses.Queries;

/// <summary>
/// Warehouse master list (spec section 15), optionally narrowed by kind, by active
/// state, and by a text search over the code and the name. Everything is data: the
/// list is whatever the administrator has created, never a fixed set.
/// </summary>
public record GetWarehousesQuery(
    WarehouseKind? Kind = null, bool? ActiveOnly = null, string? Search = null)
    : IRequest<List<WarehouseMasterDto>>;

public class GetWarehousesQueryHandler : IRequestHandler<GetWarehousesQuery, List<WarehouseMasterDto>>
{
    private readonly IApplicationDbContext _db;
    public GetWarehousesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<WarehouseMasterDto>> Handle(GetWarehousesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Warehouses.AsNoTracking().AsQueryable();

        if (request.Kind.HasValue)
            query = query.Where(w => w.Kind == request.Kind.Value);

        if (request.ActiveOnly == true)
            query = query.Where(w => w.IsActive);
        else if (request.ActiveOnly == false)
            query = query.Where(w => !w.IsActive);

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Case-insensitive on both providers the system runs on (SQL Server and the
            // in-memory test provider), so the UI search behaves the same in a test.
            var term = search.ToLowerInvariant();
            query = query.Where(w => w.Code.ToLower().Contains(term) || w.Name.ToLower().Contains(term));
        }

        var warehouses = await query.OrderBy(w => w.Code).ToListAsync(cancellationToken);
        return warehouses.Select(CreateWarehouseCommandHandler.Map).ToList();
    }
}
