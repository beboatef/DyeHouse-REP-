using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

/// <summary>
/// Computes balances by summing the append-only InventoryTransaction ledger
/// (spec section 18) - the only place "how much is left" is ever calculated.
/// </summary>
public class InventoryLedgerService : IInventoryLedgerService
{
    private readonly ApplicationDbContext _context;

    public InventoryLedgerService(ApplicationDbContext context) => _context = context;

    public async Task<(decimal Kg, decimal Meter)> GetMessageBalanceAsync(
        Guid rawMessageId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == rawMessageId && t.ItemId == itemId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        return Sum(rows.Select(r => (r.QuantityKg, r.QuantityMeter, (int)r.Direction)));
    }

    public async Task<(decimal Kg, decimal Meter)> GetCustomerBalanceAsync(
        Guid rawMessageId, Guid itemId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == rawMessageId && t.ItemId == itemId && t.CustomerId == customerId)
            .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        return Sum(rows.Select(r => (r.QuantityKg, r.QuantityMeter, (int)r.Direction)));
    }

    public async Task<List<Guid>> GetMessageIdsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _context.InventoryTransactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId && t.RawMessageId != null)
            .Select(t => t.RawMessageId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private static (decimal Kg, decimal Meter) Sum(IEnumerable<(decimal? Kg, decimal? Meter, int Direction)> rows)
    {
        decimal kg = 0, meter = 0;
        foreach (var r in rows)
        {
            kg += (r.Kg ?? 0) * r.Direction;
            meter += (r.Meter ?? 0) * r.Direction;
        }
        return (kg, meter);
    }
}
