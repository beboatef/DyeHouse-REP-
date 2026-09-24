using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Queries;

public record GetCustomersQuery(bool? ActiveOnly = null, string? Search = null) : IRequest<List<CustomerDto>>;

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, List<CustomerDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCustomersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => c.Code.Contains(request.Search) || c.Name.Contains(request.Search));

        return await query
            .OrderBy(c => c.Code)
            .Select(c => new CustomerDto { Id = c.Id, Code = c.Code, Name = c.Name, IsActive = c.IsActive })
            .ToListAsync(cancellationToken);
    }
}
