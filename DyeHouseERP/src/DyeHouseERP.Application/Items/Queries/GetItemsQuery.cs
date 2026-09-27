using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Queries;

/// <summary>
/// Item master lookup (spec section 6). Search covers code, both names and the
/// category so the same box works in Arabic or English.
/// </summary>
public record GetItemsQuery(bool? ActiveOnly = null, string? Search = null, string? Category = null)
    : IRequest<List<ItemDto>>;

public class GetItemsQueryHandler : IRequestHandler<GetItemsQuery, List<ItemDto>>
{
    private readonly IApplicationDbContext _db;
    public GetItemsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ItemDto>> Handle(GetItemsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Items.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(i => i.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(i => i.Category == request.Category);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(i =>
                i.Code.Contains(term) ||
                i.Name.Contains(term) ||
                i.NameAr.Contains(term) ||
                i.NameEn.Contains(term) ||
                (i.Category != null && i.Category.Contains(term)));
        }

        return await query
            .OrderBy(i => i.Code)
            .Select(i => new ItemDto
            {
                Id = i.Id,
                Code = i.Code,
                Name = i.Name,
                NameAr = i.NameAr,
                NameEn = i.NameEn,
                Category = i.Category,
                BaseUnit = i.BaseUnit,
                IsActive = i.IsActive
            })
            .ToListAsync(cancellationToken);
    }
}
