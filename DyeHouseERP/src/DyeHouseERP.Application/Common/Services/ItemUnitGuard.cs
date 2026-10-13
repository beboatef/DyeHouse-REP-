using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// The ONE gate every stock- or production-recording handler runs through before it
/// writes a quantity (spec sections 5-6): a movement may only use units CONFIGURED
/// on its item, and when the item requires both units BOTH measurements must be
/// supplied. KG and Meter stay fully independent - a quantity in an unconfigured
/// unit is refused, never converted.
///
/// This is deliberately a server-side helper rather than a FluentValidation rule:
/// the item configuration is per-row data, so a validator that does not touch the
/// database cannot enforce it, and enforcing it only on the front end is not enough.
/// The rule itself is the pure <see cref="ItemUnitRules"/>; this helper only loads
/// the item's configuration and applies it.
/// </summary>
public static class ItemUnitGuard
{
    /// <summary>
    /// Validates a (KG, Meter) quantity pair for one item. Throws
    /// <see cref="NotFoundException"/> when the item does not exist and a
    /// <c>DomainException</c> when the quantity is not valid for the item's units.
    /// </summary>
    public static async Task EnsureValidQuantitiesAsync(
        IApplicationDbContext db, Guid itemId, decimal? quantityKg, decimal? quantityMeter,
        CancellationToken cancellationToken, string? context = null, bool requireAnyQuantity = true)
    {
        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
            ?? throw new NotFoundException("Item", itemId);

        ItemUnitRules.EnsureValidQuantities(
            item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits,
            quantityKg, quantityMeter, item.Code, context, requireAnyQuantity);
    }

    /// <summary>
    /// Validates many lines at once, resolving every distinct item from a single
    /// query. Each tuple is (itemId, KG, Meter); the optional <paramref name="context"/>
    /// names the step in any refusal message.
    /// </summary>
    public static async Task EnsureValidQuantitiesAsync(
        IApplicationDbContext db, IEnumerable<(Guid ItemId, decimal? Kg, decimal? Meter)> lines,
        CancellationToken cancellationToken, string? context = null, bool requireAnyQuantity = true)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;

        var itemIds = list.Select(l => l.ItemId).Distinct().ToList();
        var byId = (await db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(i => i.Id);

        foreach (var line in list)
        {
            if (!byId.TryGetValue(line.ItemId, out var item))
                throw new NotFoundException("Item", line.ItemId);

            ItemUnitRules.EnsureValidQuantities(
                item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits,
                line.Kg, line.Meter, item.Code, context, requireAnyQuantity);
        }
    }

    /// <summary>
    /// Convenience overload for an already-loaded item (a workflow that holds the
    /// aggregate). Returns nothing; throws on an invalid quantity.
    /// </summary>
    public static void EnsureValidQuantities(
        Item item, decimal? quantityKg, decimal? quantityMeter, string? context = null, bool requireAnyQuantity = true)
        => ItemUnitRules.EnsureValidQuantities(
            item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits,
            quantityKg, quantityMeter, item.Code, context, requireAnyQuantity);
}
