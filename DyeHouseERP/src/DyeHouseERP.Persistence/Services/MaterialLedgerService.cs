using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

public class MaterialLedgerService : IMaterialLedgerService
{
    private readonly ApplicationDbContext _context;
    public MaterialLedgerService(ApplicationDbContext context) => _context = context;

    public async Task<decimal> GetBalanceAsync(Guid materialId, Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.MaterialTransactions.AsNoTracking()
            .Where(t => t.MaterialId == materialId && t.WarehouseId == warehouseId)
            .Select(t => new { t.Quantity, t.Direction })
            .ToListAsync(cancellationToken);

        return rows.Sum(r => r.Quantity * (int)r.Direction);
    }
}
