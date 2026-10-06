using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.PurchaseUnits.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PurchaseUnits.Queries;

public record GetPurchaseUnitsQuery(bool? ActiveOnly = null, string? Search = null) : IRequest<List<PurchaseUnitDto>>;

public class GetPurchaseUnitsQueryHandler : IRequestHandler<GetPurchaseUnitsQuery, List<PurchaseUnitDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPurchaseUnitsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PurchaseUnitDto>> Handle(GetPurchaseUnitsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.PurchaseUnits.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u => u.Code.Contains(term) || u.NameAr.Contains(term) || u.NameEn.Contains(term));
        }

        var units = await query.OrderBy(u => u.Code).ToListAsync(cancellationToken);
        var byId = units.ToDictionary(u => u.Id, u => u);

        var result = new List<PurchaseUnitDto>(units.Count);
        foreach (var unit in units)
        {
            var baseUnit = unit.BaseUnitId.HasValue && byId.TryGetValue(unit.BaseUnitId.Value, out var b) ? b : null;
            result.Add(Map(unit, baseUnit));
        }

        return result;
    }

    internal static PurchaseUnitDto Map(Domain.Entities.PurchaseUnit u, Domain.Entities.PurchaseUnit? baseUnit) => new()
    {
        Id = u.Id,
        Code = u.Code,
        NameAr = u.NameAr,
        NameEn = u.NameEn,
        IsActive = u.IsActive,
        IsSystemDefault = u.IsSystemDefault,
        ConversionFactor = u.ConversionFactor,
        BaseUnitId = u.BaseUnitId,
        BaseUnitName = baseUnit?.NameEn,
        BaseUnitNameAr = baseUnit?.NameAr,
        Notes = u.Notes
    };
}
