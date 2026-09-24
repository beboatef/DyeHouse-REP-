using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Queries;

public record GetDeliveriesQuery(Guid? CustomerId = null, DeliveryStatus? Status = null) : IRequest<List<DeliveryDto>>;
public record GetDeliveryByIdQuery(Guid Id) : IRequest<DeliveryDto>;

public class GetDeliveriesQueryHandler : IRequestHandler<GetDeliveriesQuery, List<DeliveryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetDeliveriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<DeliveryDto>> Handle(GetDeliveriesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Deliveries.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(d => d.CustomerId == request.CustomerId);
        if (request.Status.HasValue) query = query.Where(d => d.Status == request.Status);

        var ids = await query.OrderByDescending(d => d.DeliveryDate).Select(d => d.Id).ToListAsync(cancellationToken);

        var result = new List<DeliveryDto>();
        foreach (var id in ids) result.Add(await LoadDtoAsync(_db, id, cancellationToken));
        return result;
    }

    internal static async Task<DeliveryDto> LoadDtoAsync(IApplicationDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Delivery", id);

        var lines = await db.DeliveryLines.AsNoTracking().Where(l => l.DeliveryId == id).ToListAsync(cancellationToken);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == delivery.CustomerId, cancellationToken);
        var orderIds = lines.Select(l => l.ProductionOrderId).Distinct().ToList();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        return new DeliveryDto
        {
            Id = delivery.Id, DeliveryNumber = delivery.DeliveryNumber, DeliveryDate = delivery.DeliveryDate,
            CustomerId = delivery.CustomerId, CustomerCode = customer?.Code ?? "", CustomerName = customer?.Name ?? "",
            Status = delivery.Status, Notes = delivery.Notes,
            Lines = lines.Select(l => new DeliveryLineDto
            {
                Id = l.Id, ProductionOrderId = l.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(l.ProductionOrderId)?.OrderNumber ?? "",
                ItemId = l.ItemId, ItemCode = items.GetValueOrDefault(l.ItemId)?.Code ?? "", Color = l.Color,
                QuantityKg = l.QuantityKg, QuantityMeter = l.QuantityMeter, PieceCount = l.PieceCount, RawOrigin = l.RawOrigin
            }).ToList()
        };
    }
}

public class GetDeliveryByIdQueryHandler : IRequestHandler<GetDeliveryByIdQuery, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    public GetDeliveryByIdQueryHandler(IApplicationDbContext db) => _db = db;
    public Task<DeliveryDto> Handle(GetDeliveryByIdQuery request, CancellationToken cancellationToken)
        => GetDeliveriesQueryHandler.LoadDtoAsync(_db, request.Id, cancellationToken);
}
