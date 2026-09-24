using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerTransfers.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerTransfers.Queries;

public record GetCustomerTransfersQuery(Guid? CustomerId = null) : IRequest<List<CustomerTransferDto>>;

public class GetCustomerTransfersQueryHandler : IRequestHandler<GetCustomerTransfersQuery, List<CustomerTransferDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCustomerTransfersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CustomerTransferDto>> Handle(GetCustomerTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = _db.CustomerTransfers.AsNoTracking().AsQueryable();
        if (request.CustomerId.HasValue)
            query = query.Where(t => t.FromCustomerId == request.CustomerId || t.ToCustomerId == request.CustomerId);

        var transfers = await query.OrderByDescending(t => t.TransferDate).ToListAsync(cancellationToken);
        if (transfers.Count == 0) return new List<CustomerTransferDto>();

        var customerIds = transfers.SelectMany(t => new[] { t.FromCustomerId, t.ToCustomerId }).Distinct().ToList();
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var items = await _db.Items.AsNoTracking().Where(i => transfers.Select(t => t.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var messages = await _db.RawMessages.AsNoTracking().Where(m => transfers.Select(t => t.RawMessageId).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);

        return transfers.Select(t => new CustomerTransferDto
        {
            Id = t.Id,
            TransferNumber = t.TransferNumber,
            TransferDate = t.TransferDate,
            FromCustomerId = t.FromCustomerId,
            FromCustomerCode = customers.GetValueOrDefault(t.FromCustomerId)?.Code ?? string.Empty,
            FromCustomerName = customers.GetValueOrDefault(t.FromCustomerId)?.Name ?? string.Empty,
            ToCustomerId = t.ToCustomerId,
            ToCustomerCode = customers.GetValueOrDefault(t.ToCustomerId)?.Code ?? string.Empty,
            ToCustomerName = customers.GetValueOrDefault(t.ToCustomerId)?.Name ?? string.Empty,
            RawMessageId = t.RawMessageId,
            MessageNumber = messages.GetValueOrDefault(t.RawMessageId)?.MessageNumber ?? string.Empty,
            ItemId = t.ItemId,
            ItemCode = items.GetValueOrDefault(t.ItemId)?.Code ?? string.Empty,
            ItemName = items.GetValueOrDefault(t.ItemId)?.Name ?? string.Empty,
            QuantityKg = t.QuantityKg,
            QuantityMeter = t.QuantityMeter,
            Reason = t.Reason,
            Notes = t.Notes,
            CreatedBy = t.CreatedBy,
            CreatedAtUtc = t.CreatedAtUtc
        }).ToList();
    }
}
