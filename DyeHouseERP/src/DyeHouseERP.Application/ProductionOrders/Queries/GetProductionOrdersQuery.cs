using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Queries;

public record GetProductionOrdersQuery(Guid? CustomerId = null, ProductionOrderStatus? Status = null)
    : IRequest<List<ProductionOrderDto>>;

public class GetProductionOrdersQueryHandler : IRequestHandler<GetProductionOrdersQuery, List<ProductionOrderDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionOrderDto>> Handle(GetProductionOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionOrders.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue) query = query.Where(o => o.CustomerId == request.CustomerId);
        if (request.Status.HasValue) query = query.Where(o => o.Status == request.Status);

        var ids = await query.OrderByDescending(o => o.OrderDate).Select(o => o.Id).ToListAsync(cancellationToken);

        // NOTE: this loads each order's full detail via the shared loader for
        // scaffold simplicity. Once the production floor dashboard (spec
        // section 37) is built, replace with a purpose-built lightweight
        // projection - N+1-ish detail loads aren't meant to be the final word.
        var result = new List<ProductionOrderDto>();
        foreach (var id in ids)
            result.Add(await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, id, cancellationToken));

        return result;
    }
}
