using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// The ONE set of rules a sub-customer permit obeys (spec section 31), shared by the two handlers that
/// touch it - issuing it (CreateDeliveryCommandHandler) and delivering it (MarkDeliveryDeliveredCommandHandler)
/// - so the two can never drift apart:
///
///   * it is ALWAYS linked to a real primary permit, which is the permit that owns the single physical
///     inventory movement;
///   * its lines are a SLICE of that permit, never additional goods: what it claims, per (Job Order, Item),
///     may never exceed what the primary permit delivered for that same job order, counting every other
///     live sub-permit against the same allowance;
///   * and - because the goods only physically exist once the primary permit has been delivered - a
///     sub-permit may only be DELIVERED after that permit was delivered.
///
/// The rules are deliberately re-checked when the sub-permit is DELIVERED and not only when it is created:
/// a primary permit can still be corrected (which lowers what it delivered), a sibling sub-permit can be
/// cancelled and re-issued, and a document may sit in Prepared for days. Validating at the moment the
/// document takes effect is the same discipline the primary permit itself follows, where the ready-stock
/// balance is only ever checked as the delivery posts.
/// </summary>
public static class SubCustomerPermitGuard
{
    /// <summary>
    /// Loads the primary permit a sub-customer permit is issued against, refusing one that cannot back it.
    /// A cancelled primary permit can never be sliced; a null id means "not a sub-permit" and returns null.
    /// </summary>
    public static async Task<Delivery?> ResolvePrimaryAsync(
        IApplicationDbContext db, Guid? primaryDeliveryId, CancellationToken cancellationToken)
    {
        if (!primaryDeliveryId.HasValue) return null;

        var primary = await db.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == primaryDeliveryId.Value, cancellationToken)
            ?? throw new NotFoundException("Delivery", primaryDeliveryId.Value);

        if (primary.Status == DeliveryStatus.Cancelled)
            throw new DomainException(
                $"Primary permit {primary.DeliveryNumber} is cancelled, so no sub-customer permit can be issued against it.");

        return primary;
    }

    /// <summary>
    /// The lifecycle rule both permit kinds share: the goods a sub-customer permit hands over are a slice of
    /// the PRIMARY permit's own delivery, so that permit must actually have been delivered. A permit that is
    /// still a draft or merely prepared has posted nothing, and "delivering" a slice of nothing would put the
    /// sub-customer's document ahead of the physical movement it claims to describe.
    /// </summary>
    public static void EnsurePrimaryIsDelivered(Delivery primary)
    {
        if (primary.Status != DeliveryStatus.Delivered)
            throw new DomainException(
                $"Primary permit {primary.DeliveryNumber} is {primary.Status}, not Delivered: its goods have not left the " +
                "ready-goods warehouse yet, so a sub-customer permit issued against it cannot be delivered either. " +
                "Deliver the primary permit first. لا يمكن تسليم إذن فرعي قبل تسليم الإذن الأساسي الذي يخصم المخزون فعليًا.");
    }

    /// <summary>
    /// Bounds a sub-customer permit by the primary permit it slices: the quantities it claims, per
    /// (Job Order, Item), may never exceed what the primary permit delivered for that same Job Order, and the
    /// total already claimed by every other NON-cancelled sub-permit counts against the same allowance.
    ///
    /// A line naming the SUB-customer's own Job Order has no primary line to measure against, so it is not
    /// bounded here - only the primary customer's own job is. KG and Meter are bounded INDEPENDENTLY and are
    /// never converted into one another.
    ///
    /// This is a DOCUMENT rule: it posts no stock and changes no balance.
    /// </summary>
    /// <param name="excludeSubPermitId">
    /// The sub-permit being re-checked (its own already-stored lines must not count as a sibling claim against
    /// itself). Null when a new permit is being issued.
    /// </param>
    public static async Task EnsureWithinPrimaryAsync(
        IApplicationDbContext db, Delivery primary,
        IReadOnlyCollection<(Guid ProductionOrderId, Guid ItemId, decimal Kg, decimal Meter)> lines,
        CancellationToken cancellationToken, Guid? excludeSubPermitId = null)
    {
        if (lines.Count == 0) return;

        var primaryLines = await db.DeliveryLines.AsNoTracking()
            .Where(l => l.DeliveryId == primary.Id)
            .Select(l => new { l.ProductionOrderId, l.ItemId, l.QuantityKg, l.QuantityMeter })
            .ToListAsync(cancellationToken);

        if (primaryLines.Count == 0) return;

        var siblingIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.PrimaryDeliveryId == primary.Id
                     && d.Status != DeliveryStatus.Cancelled
                     && d.Id != excludeSubPermitId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var siblingLines = siblingIds.Count == 0
            ? new List<(Guid ProductionOrderId, Guid ItemId, decimal Kg, decimal Meter)>()
            : (await db.DeliveryLines.AsNoTracking()
                .Where(l => siblingIds.Contains(l.DeliveryId))
                .Select(l => new { l.ProductionOrderId, l.ItemId, l.QuantityKg, l.QuantityMeter })
                .ToListAsync(cancellationToken))
            .Select(l => (l.ProductionOrderId, l.ItemId, Kg: l.QuantityKg ?? 0m, Meter: l.QuantityMeter ?? 0m))
            .ToList();

        const decimal tolerance = 0.001m;

        var primaryOrderIds = primaryLines.Select(p => p.ProductionOrderId).ToHashSet();

        foreach (var group in lines
            .Where(l => primaryOrderIds.Contains(l.ProductionOrderId))
            .GroupBy(l => (l.ProductionOrderId, l.ItemId)))
        {
            var allowedKg = primaryLines
                .Where(p => p.ProductionOrderId == group.Key.ProductionOrderId && p.ItemId == group.Key.ItemId)
                .Sum(p => p.QuantityKg ?? 0m);
            var allowedMeter = primaryLines
                .Where(p => p.ProductionOrderId == group.Key.ProductionOrderId && p.ItemId == group.Key.ItemId)
                .Sum(p => p.QuantityMeter ?? 0m);

            var claimedKg = siblingLines
                .Where(c => c.ProductionOrderId == group.Key.ProductionOrderId && c.ItemId == group.Key.ItemId)
                .Sum(c => c.Kg);
            var claimedMeter = siblingLines
                .Where(c => c.ProductionOrderId == group.Key.ProductionOrderId && c.ItemId == group.Key.ItemId)
                .Sum(c => c.Meter);

            var requestedKg = group.Sum(l => l.Kg);
            var requestedMeter = group.Sum(l => l.Meter);

            if (requestedKg > 0 && claimedKg + requestedKg > allowedKg + tolerance)
                throw new DomainException(
                    $"This sub-customer permit exceeds the primary permit {primary.DeliveryNumber}: it delivered " +
                    $"{allowedKg} KG for this Job Order, {claimedKg} KG is already covered by other sub-customer permits " +
                    $"and {requestedKg} KG more was requested.");

            if (requestedMeter > 0 && claimedMeter + requestedMeter > allowedMeter + tolerance)
                throw new DomainException(
                    $"This sub-customer permit exceeds the primary permit {primary.DeliveryNumber}: it delivered " +
                    $"{allowedMeter} Meter for this Job Order, {claimedMeter} Meter is already covered by other " +
                    $"sub-customer permits and {requestedMeter} Meter more was requested.");
        }
    }
}
