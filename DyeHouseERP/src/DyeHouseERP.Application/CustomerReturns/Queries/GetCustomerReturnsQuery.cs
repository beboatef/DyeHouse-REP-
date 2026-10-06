using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerReturns.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerReturns.Queries;

public record GetCustomerReturnsQuery(
    Guid? CustomerId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? Search = null) : IRequest<List<CustomerReturnDto>>;

public record GetCustomerReturnByIdQuery(Guid Id) : IRequest<CustomerReturnDto>;

public class GetCustomerReturnByIdQueryHandler : IRequestHandler<GetCustomerReturnByIdQuery, CustomerReturnDto>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerReturnByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public Task<CustomerReturnDto> Handle(GetCustomerReturnByIdQuery request, CancellationToken cancellationToken)
        => GetCustomerReturnsQueryHandler.LoadDtoAsync(_db, request.Id, cancellationToken);
}

public class GetCustomerReturnsQueryHandler : IRequestHandler<GetCustomerReturnsQuery, List<CustomerReturnDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerReturnsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CustomerReturnDto>> Handle(GetCustomerReturnsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.CustomerReturns.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue) query = query.Where(r => r.CustomerId == request.CustomerId.Value);
        if (request.FromDate.HasValue) query = query.Where(r => r.ReturnDate >= request.FromDate.Value);
        if (request.ToDate.HasValue) query = query.Where(r => r.ReturnDate <= request.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(r => r.ReturnNumber.Contains(term) || (r.Reason != null && r.Reason.Contains(term)));
        }

        var returns = await query
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        return await BuildAsync(_db, returns, cancellationToken);
    }

    /// <summary>Loads one return (with traceability) - used by the create command to return its result.</summary>
    internal static async Task<CustomerReturnDto> LoadDtoAsync(
        IApplicationDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var customerReturn = await db.CustomerReturns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("CustomerReturn", id);

        var dtos = await BuildAsync(db, new List<CustomerReturn> { customerReturn }, cancellationToken);
        return dtos[0];
    }

    private static async Task<List<CustomerReturnDto>> BuildAsync(
        IApplicationDbContext db, List<CustomerReturn> returns, CancellationToken cancellationToken)
    {
        if (returns.Count == 0) return new List<CustomerReturnDto>();

        var returnIds = returns.Select(r => r.Id).ToList();
        var lines = await db.CustomerReturnLines.AsNoTracking()
            .Where(l => returnIds.Contains(l.CustomerReturnId))
            .ToListAsync(cancellationToken);

        var customerIds = returns.Select(r => r.CustomerId).Distinct().ToList();
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var messageIds = lines.Select(l => l.RawMessageId).Distinct().ToList();
        var orderIds = lines.Where(l => l.ProductionOrderId.HasValue).Select(l => l.ProductionOrderId!.Value).Distinct().ToList();
        var groupIds = lines.Where(l => l.FormationGroupId.HasValue).Select(l => l.FormationGroupId!.Value).Distinct().ToList();

        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var items = await db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var messages = await db.RawMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MessageNumber })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var groups = await db.FormationGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.GroupNumber })
            .ToDictionaryAsync(g => g.Id, cancellationToken);

        var linesByReturn = lines.ToLookup(l => l.CustomerReturnId);
        var result = new List<CustomerReturnDto>(returns.Count);

        foreach (var r in returns)
        {
            customers.TryGetValue(r.CustomerId, out var customer);

            var lineDtos = new List<CustomerReturnLineDto>();
            foreach (var l in linesByReturn[r.Id])
            {
                items.TryGetValue(l.ItemId, out var item);
                messages.TryGetValue(l.RawMessageId, out var message);

                string? orderNumber = null;
                if (l.ProductionOrderId.HasValue && orders.TryGetValue(l.ProductionOrderId.Value, out var order))
                    orderNumber = order.OrderNumber;

                int? groupNumber = null;
                if (l.FormationGroupId.HasValue && groups.TryGetValue(l.FormationGroupId.Value, out var group))
                    groupNumber = group.GroupNumber;

                lineDtos.Add(new CustomerReturnLineDto
                {
                    Id = l.Id,
                    RawMessageId = l.RawMessageId,
                    MessageNumber = message?.MessageNumber ?? string.Empty,
                    ItemId = l.ItemId,
                    ItemCode = item?.Code ?? string.Empty,
                    ItemName = item?.Name ?? string.Empty,
                    QuantityKg = l.QuantityKg,
                    QuantityMeter = l.QuantityMeter,
                    ProductionOrderId = l.ProductionOrderId,
                    ProductionOrderNumber = orderNumber,
                    FormationGroupId = l.FormationGroupId,
                    FormationGroupNumber = groupNumber,
                    Notes = l.Notes
                });
            }

            result.Add(new CustomerReturnDto
            {
                Id = r.Id,
                ReturnNumber = r.ReturnNumber,
                ReturnDate = r.ReturnDate,
                CustomerId = r.CustomerId,
                CustomerCode = customer?.Code ?? string.Empty,
                CustomerName = customer?.Name ?? string.Empty,
                Reason = r.Reason,
                Notes = r.Notes,
                CreatedBy = r.CreatedBy,
                CreatedAtUtc = r.CreatedAtUtc,
                Lines = lineDtos
            });
        }

        return result;
    }
}
