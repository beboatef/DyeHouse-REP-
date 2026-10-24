using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// H1 - the ONE gate every production-start / production-transition path runs
/// through: raw material must be 100% allocated before production starts, before a
/// stage records output, before ready-goods stock is created and before an order is
/// completed (spec section 13).
///
/// The rule itself lives on the aggregate
/// (<see cref="ProductionOrder.EnsureRawAllocationComplete(decimal, decimal)"/>).
/// This helper only supplies the allocation totals from the database, so a handler
/// can enforce the rule without having to load - or remember to load - the order's
/// allocation rows, which live in their own table and are NOT part of a plainly
/// loaded order.
///
/// Deliberately a single entry point: a path that does not call it is a path that
/// bypasses the rule, and the call sites below are the complete set of handlers that
/// can start production, record stage output, post ready-goods stock or complete an
/// order (start / transfer to next stage / complete stage / edit or skip a stage /
/// ready-goods transfer / complete order / resume).
///
/// Concurrency: allocation rows are APPEND-ONLY (nothing in the system edits or
/// deletes a RawAllocation), so the check is monotonic - it can only ever become
/// MORE complete. A concurrent allocation racing this check can therefore cause a
/// false refusal at worst, never a bypass; no extra lock or transaction is needed.
/// </summary>
public static class RawAllocationGuard
{
    /// <summary>
    /// Refuses the operation unless 100% of the order's requested quantity is allocated.
    /// </summary>
    /// <param name="additionalQuantityKg">
    /// Raw material this SAME operation is about to allocate (the resume re-issue). It is
    /// not persisted yet, but the order will hold it the moment the operation commits, so
    /// it counts towards the 100%. Zero for every path that allocates nothing.
    /// </param>
    /// <param name="additionalQuantityMeter">Meter counterpart of <paramref name="additionalQuantityKg"/>.</param>
    public static async Task EnsureCompleteAsync(
        IApplicationDbContext db, ProductionOrder order, CancellationToken cancellationToken,
        decimal additionalQuantityKg = 0m, decimal additionalQuantityMeter = 0m)
    {
        var allocated = await db.RawAllocations.AsNoTracking()
            .Where(a => a.ProductionOrderId == order.Id)
            .Select(a => new { a.QuantityKg, a.QuantityMeter })
            .ToListAsync(cancellationToken);

        order.EnsureRawAllocationComplete(
            allocated.Sum(a => a.QuantityKg ?? 0m) + additionalQuantityKg,
            allocated.Sum(a => a.QuantityMeter ?? 0m) + additionalQuantityMeter);
    }
}
