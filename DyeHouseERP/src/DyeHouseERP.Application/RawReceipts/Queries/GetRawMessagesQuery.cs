using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawReceipts.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawReceipts.Queries;

public record GetRawMessagesQuery(Guid? CustomerId = null, Guid? ItemId = null, bool OnlyWithBalance = false)
    : IRequest<List<RawMessageDto>>;

/// <summary>
/// Returns messages together with their REMAINING balance per line, computed
/// live from the inventory ledger (spec section 18) - never from a stored
/// balance field. This is what powers the manual "choose which message to
/// allocate from" picker required by spec section 4 (no FIFO).
///
/// When CustomerId is supplied, both the message list AND the balance shown
/// are scoped to that customer's own ownership of each message (spec
/// section 15 - Customer-to-Customer Transfer): a message this customer
/// never originally received will still show up here if some quantity was
/// transferred TO them, and the balance shown is only their portion - never
/// the whole message's total remaining stock.
/// </summary>
public class GetRawMessagesQueryHandler : IRequestHandler<GetRawMessagesQuery, List<RawMessageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryLedgerService _ledger;

    public GetRawMessagesQueryHandler(IApplicationDbContext db, IInventoryLedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<List<RawMessageDto>> Handle(GetRawMessagesQuery request, CancellationToken cancellationToken)
    {
        var messagesQuery = _db.RawMessages.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue)
        {
            // Union of "originally received by this customer" and
            // "transferred to this customer at some point" - both live in
            // the ledger's CustomerId dimension (see IInventoryLedgerService).
            var relevantMessageIds = await _ledger.GetMessageIdsForCustomerAsync(request.CustomerId.Value, cancellationToken);
            messagesQuery = messagesQuery.Where(m => relevantMessageIds.Contains(m.Id));
        }

        var messages = await messagesQuery.OrderByDescending(m => m.ReceiptDate).ToListAsync(cancellationToken);
        if (messages.Count == 0) return new List<RawMessageDto>();

        var messageIds = messages.Select(m => m.Id).ToList();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => messages.Select(m => m.CustomerId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => messages.Select(m => m.WarehouseId).Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        var itemIds = messages.SelectMany(m => m.Lines).Select(l => l.ItemId).Distinct().ToList();
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        // Sum ledger movements per (RawMessageId, ItemId[, Customer]) to get the remaining balance.
        var ledgerQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId != null && messageIds.Contains(t.RawMessageId.Value));

        if (request.CustomerId.HasValue)
            ledgerQuery = ledgerQuery.Where(t => t.CustomerId == request.CustomerId);

        var ledger = await ledgerQuery
            .GroupBy(t => new { t.RawMessageId, t.ItemId })
            .Select(g => new
            {
                g.Key.RawMessageId,
                g.Key.ItemId,
                Kg = g.Sum(t => (t.QuantityKg ?? 0) * (int)t.Direction),
                Meter = g.Sum(t => (t.QuantityMeter ?? 0) * (int)t.Direction)
            })
            .ToListAsync(cancellationToken);

        var balances = ledger.ToDictionary(x => (x.RawMessageId, x.ItemId), x => (x.Kg, x.Meter));

        var result = messages.Select(m => new RawMessageDto
        {
            Id = m.Id,
            MessageNumber = m.MessageNumber,
            ReceiptDate = m.ReceiptDate,
            CustomerId = m.CustomerId,
            CustomerCode = customers.GetValueOrDefault(m.CustomerId)?.Code ?? string.Empty,
            CustomerName = customers.GetValueOrDefault(m.CustomerId)?.Name ?? string.Empty,
            WarehouseId = m.WarehouseId,
            WarehouseName = warehouses.GetValueOrDefault(m.WarehouseId)?.Name ?? string.Empty,
            ReceivingUser = m.ReceivingUser,
            Notes = m.Notes,
            InspectionStatus = m.InspectionStatus,
            Status = m.Status,
            Lines = m.Lines
                .Where(l => !request.ItemId.HasValue || l.ItemId == request.ItemId)
                .Select(l =>
                {
                    balances.TryGetValue((m.Id, l.ItemId), out var bal);
                    return new RawMessageLineDto
                    {
                        Id = l.Id,
                        ItemId = l.ItemId,
                        ItemCode = items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty,
                        ItemName = items.GetValueOrDefault(l.ItemId)?.Name ?? string.Empty,
                        QuantityKg = l.QuantityKg,
                        QuantityMeter = l.QuantityMeter,
                        PieceCount = l.PieceCount,
                        Notes = l.Notes,
                        RemainingKg = l.QuantityKg.HasValue ? bal.Kg : null,
                        RemainingMeter = l.QuantityMeter.HasValue ? bal.Meter : null
                    };
                })
                .ToList()
        }).ToList();

        if (request.OnlyWithBalance)
            result = result.Where(m => m.Lines.Any(l => (l.RemainingKg ?? 0) > 0 || (l.RemainingMeter ?? 0) > 0)).ToList();

        return result;
    }
}
