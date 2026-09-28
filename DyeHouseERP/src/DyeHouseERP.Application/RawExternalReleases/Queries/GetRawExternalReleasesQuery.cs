using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawExternalReleases.Queries;

public record GetRawExternalReleasesQuery(Guid? CustomerId = null, RawReleaseReason? Reason = null,
    ExternalProcessingStatus? Status = null) : IRequest<List<RawExternalReleaseDto>>;

public class GetRawExternalReleasesQueryHandler : IRequestHandler<GetRawExternalReleasesQuery, List<RawExternalReleaseDto>>
{
    private readonly IApplicationDbContext _db;
    public GetRawExternalReleasesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<RawExternalReleaseDto>> Handle(GetRawExternalReleasesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.RawExternalReleases.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId);
        if (request.Reason is not null) query = query.Where(r => r.Reason == request.Reason);
        if (request.Status is not null) query = query.Where(r => r.Status == request.Status);

        var releases = await query.OrderByDescending(r => r.ReleaseDate).ToListAsync(cancellationToken);
        if (releases.Count == 0) return new List<RawExternalReleaseDto>();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => releases.Select(r => r.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking()
            .Where(i => releases.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking()
            .Where(m => releases.Select(r => r.RawMessageId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        var orderIds = releases.Where(r => r.ProductionOrderId is not null).Select(r => r.ProductionOrderId!.Value).Distinct().ToList();
        var orders = await _db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

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
            CreatedAtUtc = r.CreatedAtUtc,
            ProductionOrderId = r.ProductionOrderId,
            ProductionOrderNumber = r.ProductionOrderId is not null ? orders.GetValueOrDefault(r.ProductionOrderId.Value) : null,
            ExternalProcessingStage = r.ExternalProcessingStage,
            ExternalProcessingCost = r.ExternalProcessingCost,
            ExpectedReturnDate = r.ExpectedReturnDate,
            ActualReturnDate = r.ActualReturnDate,
            ReturnedQuantityKg = r.ReturnedQuantityKg,
            ReturnedQuantityMeter = r.ReturnedQuantityMeter,
            CancellationReason = r.CancellationReason,
            Status = r.Status
        }).ToList();
    }
}
