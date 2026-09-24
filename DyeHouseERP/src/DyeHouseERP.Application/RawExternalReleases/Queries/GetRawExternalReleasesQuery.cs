using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawExternalReleases.Queries;

public record GetRawExternalReleasesQuery(Guid? CustomerId = null) : IRequest<List<RawExternalReleaseDto>>;

public class GetRawExternalReleasesQueryHandler : IRequestHandler<GetRawExternalReleasesQuery, List<RawExternalReleaseDto>>
{
    private readonly IApplicationDbContext _db;
    public GetRawExternalReleasesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<RawExternalReleaseDto>> Handle(GetRawExternalReleasesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.RawExternalReleases.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId);

        var releases = await query.OrderByDescending(r => r.ReleaseDate).ToListAsync(cancellationToken);
        if (releases.Count == 0) return new List<RawExternalReleaseDto>();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => releases.Select(r => r.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking()
            .Where(i => releases.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking()
            .Where(m => releases.Select(r => r.RawMessageId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        return releases.Select(r => new RawExternalReleaseDto
        {
            Id = r.Id,
            ReleaseNumber = r.ReleaseNumber,
            ReleaseDate = r.ReleaseDate,
            CustomerId = r.CustomerId,
            CustomerCode = customers.GetValueOrDefault(r.CustomerId)?.Code ?? string.Empty,
            CustomerName = customers.GetValueOrDefault(r.CustomerId)?.Name ?? string.Empty,
            ItemId = r.ItemId,
            ItemCode = items.GetValueOrDefault(r.ItemId)?.Code ?? string.Empty,
            ItemName = items.GetValueOrDefault(r.ItemId)?.Name ?? string.Empty,
            RawMessageId = r.RawMessageId,
            MessageNumber = messages.GetValueOrDefault(r.RawMessageId)?.MessageNumber ?? string.Empty,
            QuantityKg = r.QuantityKg,
            QuantityMeter = r.QuantityMeter,
            Reason = r.Reason,
            ExternalParty = r.ExternalParty,
            Notes = r.Notes,
            CreatedBy = r.CreatedBy,
            CreatedAtUtc = r.CreatedAtUtc
        }).ToList();
    }
}
