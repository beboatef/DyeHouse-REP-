using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Domain.Entities;
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

        // Resolve from the actual executions, never copy drawing numbers onto delivery rows.
        var drawings = await db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => orderIds.Contains(s.ProductionOrderId) && s.RequiresDrawingNumber && s.DrawingNumber != null)
            .Select(s => new { s.ProductionOrderId, s.DrawingNumber }).ToListAsync(cancellationToken);

        // Remaining original raw per order (spec section 31): the order's fixed original quantity minus
        // everything its NON-cancelled deliveries have settled - the same figure the statement and the
        // create-time guard use, computed here once for every line of this permit.
        var remaining = await ReadRemainingOriginalRawAsync(db, orderIds, cancellationToken);

        // Sub-customer permit: only the primary permit's number and its customer's code/name travel
        // across - never the primary customer's own record (spec section 31).
        Delivery? primary = null;
        Customer? primaryCustomer = null;
        if (delivery.PrimaryDeliveryId.HasValue)
        {
            primary = await db.Deliveries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == delivery.PrimaryDeliveryId.Value, cancellationToken);

            if (delivery.PrimaryCustomerId.HasValue)
                primaryCustomer = await db.Customers.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == delivery.PrimaryCustomerId.Value, cancellationToken);
        }

        return new DeliveryDto
        {
            Id = delivery.Id, DeliveryNumber = delivery.DeliveryNumber, DeliveryDate = delivery.DeliveryDate,
            CustomerId = delivery.CustomerId, CustomerCode = customer?.Code ?? "", CustomerName = customer?.Name ?? "",
            Status = delivery.Status, Notes = delivery.Notes,
            IsSubCustomerPermit = delivery.IsSubCustomerPermit,
            PrimaryDeliveryId = delivery.PrimaryDeliveryId,
            PrimaryDeliveryNumber = primary?.DeliveryNumber,
            PrimaryCustomerId = delivery.PrimaryCustomerId,
            PrimaryCustomerCode = primaryCustomer?.Code,
            PrimaryCustomerName = primaryCustomer?.Name,
            Lines = lines.Select(l => new DeliveryLineDto
            {
                Id = l.Id, ProductionOrderId = l.ProductionOrderId, ProductionOrderNumber = orders.GetValueOrDefault(l.ProductionOrderId)?.OrderNumber ?? "",
                ProductionOrderDate = orders.GetValueOrDefault(l.ProductionOrderId)?.OrderDate,
                ItemId = l.ItemId, ItemCode = items.GetValueOrDefault(l.ItemId)?.Code ?? "",
                ItemName = items.GetValueOrDefault(l.ItemId)?.Name ?? "", Color = l.Color, Category = l.Category.ToString(),
                DrawingNumbers = drawings.Where(s => s.ProductionOrderId == l.ProductionOrderId)
                    .Select(s => s.DrawingNumber!).Distinct().ToList(),
                QuantityKg = l.QuantityKg, QuantityMeter = l.QuantityMeter, PieceCount = l.PieceCount, RawOrigin = l.RawOrigin,
                OriginalRawQuantityKg = l.OriginalRawQuantityKg,
                OriginalRawQuantityMeter = l.OriginalRawQuantityMeter,
                OriginalOrderRawQuantityKg = orders.GetValueOrDefault(l.ProductionOrderId)?.RequestedQuantityKg,
                OriginalOrderRawQuantityMeter = orders.GetValueOrDefault(l.ProductionOrderId)?.RequestedQuantityMeter,
                RemainingOriginalRawKg = remaining.GetValueOrDefault(l.ProductionOrderId).Kg,
                RemainingOriginalRawMeter = remaining.GetValueOrDefault(l.ProductionOrderId).Meter
            }).ToList()
        };
    }

    /// <summary>
    /// Remaining original raw per Job Order = the order's ORIGINAL quantity (fixed, never overwritten)
    /// minus every original-raw quantity its non-cancelled deliveries have already settled. KG and Meter
    /// are computed independently; neither ever stands in for the other.
    /// </summary>
    internal static async Task<Dictionary<Guid, (decimal Kg, decimal Meter)>> ReadRemainingOriginalRawAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, (decimal Kg, decimal Meter)>();
        if (orderIds.Count == 0) return result;

        var cancelledDeliveryIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Cancelled)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        // A SUB-customer permit (spec section 31) owns no physical movement and settles NO original raw
        // of its own: its figures describe a share of the PRIMARY permit's delivery. Counting them here
        // would settle the same original raw twice and shrink the order's remaining quantity for real
        // deliveries - so ONLY movements that actually delivered stock reduce what remains.
        var subCustomerPermitIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.PrimaryDeliveryId != null)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var settled = await db.DeliveryLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.ProductionOrderId)
                     && !cancelledDeliveryIds.Contains(l.DeliveryId)
                     && !subCustomerPermitIds.Contains(l.DeliveryId))
            .Select(l => new { l.ProductionOrderId, l.OriginalRawQuantityKg, l.OriginalRawQuantityMeter })
            .ToListAsync(cancellationToken);

        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.RequestedQuantityKg, o.RequestedQuantityMeter })
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            var settledKg = settled.Where(s => s.ProductionOrderId == order.Id).Sum(s => s.OriginalRawQuantityKg ?? 0m);
            var settledMeter = settled.Where(s => s.ProductionOrderId == order.Id).Sum(s => s.OriginalRawQuantityMeter ?? 0m);

            var kg = (order.RequestedQuantityKg ?? 0m) - settledKg;
            var meter = (order.RequestedQuantityMeter ?? 0m) - settledMeter;
            result[order.Id] = (kg < 0m ? 0m : kg, meter < 0m ? 0m : meter);
        }

        return result;
    }
}

public class GetDeliveryByIdQueryHandler : IRequestHandler<GetDeliveryByIdQuery, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    public GetDeliveryByIdQueryHandler(IApplicationDbContext db) => _db = db;
    public Task<DeliveryDto> Handle(GetDeliveryByIdQuery request, CancellationToken cancellationToken)
        => GetDeliveriesQueryHandler.LoadDtoAsync(_db, request.Id, cancellationToken);
}
