using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Suppliers.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Suppliers.Queries;

/// <summary>Supplier master list (spec section 35) - used by the checks register today and purchases next.</summary>
public record GetSuppliersQuery(bool? ActiveOnly = null, string? Search = null) : IRequest<List<SupplierDto>>;

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, List<SupplierDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSuppliersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.Name.Contains(term) ||
                s.NameAr.Contains(term) || s.NameEn.Contains(term) || s.AccountNumber.Contains(term));
        }

        return await query
            .OrderBy(s => s.Code)
            .Select(s => new SupplierDto
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                NameAr = s.NameAr,
                NameEn = s.NameEn,
                AccountNumber = s.AccountNumber,
                Phone = s.Phone,
                Address = s.Address,
                ContactPerson = s.ContactPerson,
                TaxNumber = s.TaxNumber,
                IsActive = s.IsActive
            })
            .ToListAsync(cancellationToken);
    }
}
