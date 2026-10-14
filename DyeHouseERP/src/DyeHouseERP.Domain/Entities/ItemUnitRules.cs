using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Pure domain rules for an item's structured unit configuration (spec section 5).
///
/// An item has a primary/base unit and an OPTIONAL secondary unit. Both, when
/// present, must be different - KG and Meter are independent measurements and are
/// never converted into each other. A per-item setting (<c>RequireBothUnits</c>)
/// decides whether a movement must provide BOTH measurements or just one.
///
/// These rules are pure (no database, no infrastructure) so they can be unit
/// tested directly and can never be bypassed by a caller that only talks to the
/// entity. The server-side handlers apply them through <c>ItemUnitGuard</c>, which
/// is what makes the front end non-authoritative.
/// </summary>
public static class ItemUnitRules
{
    /// <summary>The units the item actually uses: the base unit, plus the secondary unit when configured.</summary>
    public static IReadOnlyList<UnitOfMeasure> ConfiguredUnits(UnitOfMeasure baseUnit, UnitOfMeasure? secondaryUnit)
    {
        var units = new List<UnitOfMeasure> { baseUnit };
        if (secondaryUnit.HasValue && secondaryUnit.Value != baseUnit)
            units.Add(secondaryUnit.Value);
        return units;
    }

    /// <summary>
    /// A configuration is valid only when the secondary unit (if any) differs from
    /// the base unit, and when "require both" is not switched on without a secondary
    /// unit actually being configured. Refusals name the item so the message is
    /// actionable from the UI, the import report or the API.
    /// </summary>
    public static void EnsureValidConfiguration(
        UnitOfMeasure baseUnit, UnitOfMeasure? secondaryUnit, bool requireBothUnits, string itemCode)
    {
        var code = string.IsNullOrWhiteSpace(itemCode) ? "(unnamed)" : itemCode;

        if (secondaryUnit.HasValue && secondaryUnit.Value == baseUnit)
            throw new DomainException(
                $"Item '{code}': the secondary unit cannot be the same as the base unit ({UnitName(baseUnit)}). " +
                "KG and Meter are independent; an item either uses one unit, or two DIFFERENT units.");

        if (requireBothUnits && !secondaryUnit.HasValue)
            throw new DomainException(
                $"Item '{code}': 'require both units' cannot be enabled without configuring a secondary unit.");
    }

    /// <summary>
    /// Validates a (KG, Meter) quantity pair against the item's configuration:
    ///   * a quantity in a unit the item does not use is refused (never converted);
    ///   * when the workflow needs a quantity, at least one configured quantity is required;
    ///   * when the item requires both units, BOTH must be supplied.
    /// </summary>
    /// <param name="requireAnyQuantity">
    /// True for every stock- or production-recording workflow. Kept explicit so a
    /// read/derived path can validate units without demanding a quantity.
    /// </param>
    /// <param name="enforceRequireBothUnits">
    /// True everywhere a MOVEMENT or a production figure is recorded: the item's "require
    /// both units" setting is a posting rule and stays enforced. False only for a document that
    /// owns no movement at all (a sub-customer permit, which is a slice of a permit that already
    /// recorded both units): there the sub-customer may receive the one unit they asked for, and
    /// a meter-only line must not be refused merely because the item's setting says "both".
    /// Even then the unit-selection rules above still apply - a quantity in a unit the item does
    /// not use is still refused, and nothing is ever converted.
    /// </param>
    public static void EnsureValidQuantities(
        UnitOfMeasure baseUnit, UnitOfMeasure? secondaryUnit, bool requireBothUnits,
        decimal? quantityKg, decimal? quantityMeter, string itemCode,
        string? context = null, bool requireAnyQuantity = true, bool enforceRequireBothUnits = true)
    {
        var code = string.IsNullOrWhiteSpace(itemCode) ? "(unnamed)" : itemCode;
        var where = string.IsNullOrWhiteSpace(context) ? string.Empty : $" {context}";
        var configured = ConfiguredUnits(baseUnit, secondaryUnit);

        var kgProvided = quantityKg is > 0;
        var meterProvided = quantityMeter is > 0;

        if (kgProvided && !configured.Contains(UnitOfMeasure.KG))
            throw new DomainException(
                $"Item '{code}' is not configured to use KG - its unit(s): {Describe(configured)}. " +
                $"A KG quantity cannot be posted for it{where}. KG and Meter are never converted into each other.");

        if (meterProvided && !configured.Contains(UnitOfMeasure.Meter))
            throw new DomainException(
                $"Item '{code}' is not configured to use Meter - its unit(s): {Describe(configured)}. " +
                $"A Meter quantity cannot be posted for it{where}. Meter and KG are never converted into each other.");

        if (requireAnyQuantity && !kgProvided && !meterProvided)
            throw new DomainException(
                $"A quantity is required for item '{code}' in its configured unit(s): {Describe(configured)}{where}.");

        if (requireBothUnits && enforceRequireBothUnits && secondaryUnit.HasValue)
        {
            var secondaryProvided = secondaryUnit.Value == UnitOfMeasure.KG ? kgProvided : meterProvided;
            if (!secondaryProvided)
                throw new DomainException(
                    $"Item '{code}' requires BOTH of its units: a {UnitName(baseUnit)} quantity AND a " +
                    $"{UnitName(secondaryUnit.Value)} quantity must be supplied{where}.");
        }
    }

    public static string UnitName(UnitOfMeasure unit) => unit == UnitOfMeasure.KG ? "KG" : "Meter";

    private static string Describe(IReadOnlyList<UnitOfMeasure> units) =>
        string.Join(" + ", units.Select(UnitName));
}
