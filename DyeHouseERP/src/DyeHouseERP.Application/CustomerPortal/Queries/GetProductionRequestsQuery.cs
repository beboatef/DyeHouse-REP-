using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerPortal.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerPortal.Queries;

public record GetProductionRequestsQuery(Guid? CustomerId = null, ProductionRequestStatus? Status = null) : IRequest<List<ProductionRequestDto>>;

public class GetProductionRequestsQueryHandler : IRequestHandler<GetProductionRequestsQuery, List<ProductionRequestDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionRequestDto>> Handle(GetProductionRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionRequests.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId);
        if (request.Status.HasValue) query = query.Where(r => r.Status == request.Status);

        var requests = await query.OrderByDescending(r => r.RequestDate).ToListAsync(cancellationToken);
        if (requests.Count == 0) return new List<ProductionRequestDto>();

        var customers = await _db.Customers.AsNoTracking().Where(c => requests.Select(r => r.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => requests.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var orderIds = requests.Where(r => r.ConvertedProductionOrderId.HasValue).Select(r => r.ConvertedProductionOrderId!.Value).Distinct().ToList();
        var orders = await _db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        return requests.Select(r => new ProductionRequestDto
        {
            Id = r.Id, RequestNumber = r.RequestNumber, RequestDate = r.RequestDate,
            CustomerId = r.CustomerId, CustomerCode = customers.GetValueOrDefault(r.CustomerId)?.Code ?? "",
            ItemId = r.ItemId, ItemCode = items.GetValueOrDefault(r.ItemId)?.Code ?? "", ItemName = items.GetValueOrDefault(r.ItemId)?.Name ?? "",
            Color = r.Color, RequestedQuantityKg = r.RequestedQuantityKg, RequestedQuantityMeter = r.RequestedQuantityMeter,
            Notes = r.Notes, Status = r.Status, ConvertedProductionOrderId = r.ConvertedProductionOrderId,
            ConvertedOrderNumber = r.ConvertedProductionOrderId.HasValue ? orders.GetValueOrDefault(r.ConvertedProductionOrderId.Value)?.OrderNumber : null,
            StaffNotes = r.StaffNotes
        }).ToList();
    }
}

/// <summary>Shared single-entity mapper used by the ProductionRequest commands so each doesn't duplicate the join logic.</summary>
internal static class GetProductionRequestsQueryHandlerHelper
{
    public static async Task<ProductionRequestDto> MapAsync(IApplicationDbContext db, ProductionRequest pr, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pr.CustomerId, cancellationToken);
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == pr.ItemId, cancellationToken);
        var order = pr.ConvertedProductionOrderId.HasValue
            ? await db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == pr.ConvertedProductionOrderId, cancellationToken)
            : null;

        return new ProductionRequestDto
        {
            Id = pr.Id, RequestNumber = pr.RequestNumber, RequestDate = pr.RequestDate,
            CustomerId = pr.CustomerId, CustomerCode = customer?.Code ?? "",
            ItemId = pr.ItemId, ItemCode = item?.Code ?? "", ItemName = item?.Name ?? "",
            Color = pr.Color, RequestedQuantityKg = pr.RequestedQuantityKg, RequestedQuantityMeter = pr.RequestedQuantityMeter,
            Notes = pr.Notes, Status = pr.Status, ConvertedProductionOrderId = pr.ConvertedProductionOrderId,
            ConvertedOrderNumber = order?.OrderNumber, StaffNotes = pr.StaffNotes
        };
    }
}
