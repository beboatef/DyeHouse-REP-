using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Queries;

/// <summary>
/// Materials/chemicals master. The optional <paramref name="Kind"/> filter lets
/// the operating-supplies screen show only supplies while the production
/// screens show only chemicals (spec sections 23 + 27).
/// </summary>
public record GetMaterialsQuery(bool? ActiveOnly = null, MaterialKind? Kind = null) : IRequest<List<MaterialDto>>;

public class GetMaterialsQueryHandler : IRequestHandler<GetMaterialsQuery, List<MaterialDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMaterialsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MaterialDto>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Materials.AsNoTracking().AsQueryable();
        if (request.ActiveOnly == true) query = query.Where(m => m.IsActive);
        if (request.Kind is not null) query = query.Where(m => m.Kind == request.Kind);

        var materials = await query.OrderBy(m => m.Code)
            .Select(m => new MaterialDto { Id = m.Id, Code = m.Code, Name = m.Name, Unit = m.Unit, PurchasePrice = m.PurchasePrice, IsActive = m.IsActive, Kind = m.Kind, ReorderLevel = m.ReorderLevel })
            .ToListAsync(cancellationToken);

        // Live balances come from the append-only ledger, never a stored column,
        // so the list can show what is actually on hand (spec section 10).
        var balances = await _db.MaterialTransactions.AsNoTracking()
            .GroupBy(t => t.MaterialId)
            .Select(g => new { MaterialId = g.Key, Balance = g.Sum(t => t.Direction == MaterialTransactionDirection.In ? t.Quantity : -t.Quantity) })
            .ToDictionaryAsync(x => x.MaterialId, x => x.Balance, cancellationToken);

        foreach (var material in materials)
            material.CurrentBalance = balances.GetValueOrDefault(material.Id);

        return materials;
    }
}
