using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Queries;

/// <summary>
/// Formation Request list with the filters a planner actually uses (spec sections 30 and 49):
/// customer, item, raw material message, status and a date range.
/// </summary>
public record GetFormationRequestsQuery(
    Guid? CustomerId = null,
    Guid? ItemId = null,
    Guid? RawMessageId = null,
    FormationRequestStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<List<FormationRequestDto>>;

public class GetFormationRequestsQueryHandler : IRequestHandler<GetFormationRequestsQuery, List<FormationRequestDto>>
{
    private readonly IApplicationDbContext _db;
    public GetFormationRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<FormationRequestDto>> Handle(GetFormationRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.FormationRequests.AsNoTracking()
            .Include(r => r.Groups).ThenInclude(g => g.Basins).AsQueryable();

        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId);
        if (request.ItemId.HasValue) query = query.Where(r => r.ItemId == request.ItemId);
        if (request.RawMessageId.HasValue) query = query.Where(r => r.RawMessageId == request.RawMessageId);
        if (request.Status.HasValue) query = query.Where(r => r.Status == request.Status);
        if (request.From.HasValue) query = query.Where(r => r.RequestDate >= request.From);
        if (request.To.HasValue) query = query.Where(r => r.RequestDate <= request.To);

        var requests = await query
            .OrderByDescending(r => r.RequestDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return await FormationRequestDtoBuilder.BuildManyAsync(_db, requests, cancellationToken);
    }
}

public record GetFormationRequestByIdQuery(Guid Id) : IRequest<FormationRequestDto>;

public class GetFormationRequestByIdQueryHandler : IRequestHandler<GetFormationRequestByIdQuery, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    public GetFormationRequestByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<FormationRequestDto> Handle(GetFormationRequestByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationRequests.AsNoTracking()
            .Include(r => r.Groups).ThenInclude(g => g.Basins)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        return await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
    }
}

/// <summary>
/// The traceable chain for one request (spec section 31): Customer -&gt; Raw Material Message -&gt; Formation Request.
/// Formation Requests are independent documents, so the chain stops at the request itself and never reaches
/// into production; every link carries the route the UI should navigate to.
/// </summary>
public record GetFormationRequestTraceabilityQuery(Guid Id) : IRequest<FormationTraceabilityDto>;

public class GetFormationRequestTraceabilityQueryHandler
    : IRequestHandler<GetFormationRequestTraceabilityQuery, FormationTraceabilityDto>
{
    private readonly IApplicationDbContext _db;
    public GetFormationRequestTraceabilityQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<FormationTraceabilityDto> Handle(GetFormationRequestTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationRequests.AsNoTracking()
            .Include(r => r.Groups).ThenInclude(g => g.Basins)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        var dto = await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
        var links = new List<FormationTraceabilityLinkDto>
        {
            new()
            {
                Stage = "Customer",
                Reference = dto.CustomerCode,
                Detail = dto.CustomerName,
                Route = $"/customers?code={Uri.EscapeDataString(dto.CustomerCode)}",
                EntityId = dto.CustomerId
            }
        };

        if (dto.RawMessageId.HasValue)
        {
            var message = await _db.RawMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == dto.RawMessageId.Value, cancellationToken);
            links.Add(new FormationTraceabilityLinkDto
            {
                Stage = "RawMaterialMessage",
                Reference = message?.MessageNumber ?? string.Empty,
                Detail = message is null ? null : $"{message.ReceiptDate:yyyy-MM-dd} · {message.InspectionStatus}",
                Route = $"/print/raw-message/{dto.RawMessageId}",
                EntityId = dto.RawMessageId,
                DateUtc = message?.ReceiptDate
            });
        }

        links.Add(new FormationTraceabilityLinkDto
        {
            Stage = "FormationRequest",
            Reference = dto.RequestNumber,
            Detail = $"{dto.Groups.Count} group(s) · {dto.BasinCount} basin(es) · {dto.TotalQuantity} {dto.Unit}",
            Route = $"/formation-requests/{dto.Id}",
            EntityId = dto.Id,
            DateUtc = dto.RequestDate
        });

        return new FormationTraceabilityDto { Request = dto, Links = links };
    }
}
