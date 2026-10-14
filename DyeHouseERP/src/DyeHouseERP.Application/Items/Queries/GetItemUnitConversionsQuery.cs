using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Queries;

/// <summary>
/// Lists the explicit unit conversion rules configured for one item (spec sections 5-6).
/// An empty list is the normal case: KG and Meter are never converted unless a rule is
/// deliberately configured here.
/// </summary>
public record GetItemUnitConversionsQuery(Guid ItemId, string? Operation = null)
    : IRequest<List<ItemUnitConversionDto>>;

public class GetItemUnitConversionsQueryHandler
    : IRequestHandler<GetItemUnitConversionsQuery, List<ItemUnitConversionDto>>
{
    private readonly IApplicationDbContext _db;
    public GetItemUnitConversionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ItemUnitConversionDto>> Handle(
        GetItemUnitConversionsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ItemUnitConversions.AsNoTracking().Where(c => c.ItemId == request.ItemId);

        if (!string.IsNullOrWhiteSpace(request.Operation))
        {
            var operation = request.Operation.Trim();
            query = query.Where(c => c.Operation == operation);
        }

        var rules = await query.OrderBy(c => c.Operation).ToListAsync(cancellationToken);
        return rules.Select(ItemUnitConversionMapper.ToDto).ToList();
    }
}
