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
        var query = _db.FormationRequests.AsNoTracking().Include(r => r.Groups).AsQueryable();

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
            .Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        return await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
    }
}

/// <summary>
/// The full chain for one request (spec section 31): Customer -&gt; Raw Material Message -&gt; Formation Request -&gt;
/// Job Order -&gt; Production -&gt; Ready Goods -&gt; Delivery. Every link carries the route the UI should navigate to,
/// so users can walk the chain forward and backward.
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
            .Include(r => r.Groups)
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
            Detail = $"{dto.Groups.Count} group(s) · {dto.TotalQuantity} {dto.Unit}",
            Route = $"/formation-requests/{dto.Id}",
            EntityId = dto.Id,
            DateUtc = dto.RequestDate
        });

        if (dto.ProductionOrderId.HasValue)
        {
            var order = await _db.ProductionOrders.AsNoTracking()
                .Include(o => o.StageExecutions)
                .FirstOrDefaultAsync(o => o.Id == dto.ProductionOrderId.Value, cancellationToken);

            if (order is not null)
            {
                links.Add(new FormationTraceabilityLinkDto
                {
                    Stage = "JobOrder",
                    Reference = order.OrderNumber,
                    Detail = $"{order.JobOrderType} · {order.Status}",
                    Route = $"/production-orders/{order.Id}",
                    EntityId = order.Id,
                    DateUtc = order.OrderDate
                });

                // Stage names are never hard-coded (spec section 19) - resolve them from the
                // configurable stage definitions so the chain matches what production actually did.
                var stageNames = await _db.ProductionStageDefinitions.AsNoTracking()
                    .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

                foreach (var stage in order.StageExecutions.OrderBy(s => s.Sequence))
                {
                    links.Add(new FormationTraceabilityLinkDto
                    {
                        Stage = "Production",
                        Reference = stageNames.TryGetValue(stage.StageDefinitionId, out var stageName) ? stageName : $"#{stage.Sequence}",
                        Detail = stage.Status.ToString(),
                        Route = $"/production-orders/{order.Id}",
                        EntityId = stage.Id,
                        DateUtc = stage.CompletedAtUtc ?? stage.StartedAtUtc
                    });
                }

                var readyGoods = await _db.ReadyGoodsTransfers.AsNoTracking()
                    .Where(t => t.ProductionOrderId == order.Id)
                    .ToListAsync(cancellationToken);
                foreach (var transfer in readyGoods)
                {
                    links.Add(new FormationTraceabilityLinkDto
                    {
                        Stage = "ReadyGoods",
                        Reference = transfer.TransferNumber,
                        Detail = $"{transfer.QuantityKg ?? 0} KG / {transfer.QuantityMeter ?? 0} M",
                        Route = "/ready-goods",
                        EntityId = transfer.Id,
                        DateUtc = transfer.TransferDate
                    });
                }

                var deliveries = await _db.DeliveryLines.AsNoTracking()
                    .Where(l => l.ProductionOrderId == order.Id)
                    .Select(l => l.DeliveryId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var deliveryDocs = await _db.Deliveries.AsNoTracking()
                    .Where(d => deliveries.Contains(d.Id))
                    .ToListAsync(cancellationToken);

                foreach (var delivery in deliveryDocs)
                {
                    links.Add(new FormationTraceabilityLinkDto
                    {
                        Stage = "Delivery",
                        Reference = delivery.DeliveryNumber,
                        Detail = delivery.Status.ToString(),
                        Route = $"/print/delivery/{delivery.Id}",
                        EntityId = delivery.Id,
                        DateUtc = delivery.DeliveryDate
                    });
                }
            }
        }

        return new FormationTraceabilityDto { Request = dto, Links = links };
    }
}
