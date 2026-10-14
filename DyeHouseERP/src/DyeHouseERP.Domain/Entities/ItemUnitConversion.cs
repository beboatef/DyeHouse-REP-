using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// An EXPLICIT, item-scoped unit conversion rule (spec sections 5-6).
///
/// KG and Meter are independent measurements of the same stock and are NEVER
/// converted automatically: there is no global KG&lt;-&gt;Meter factor anywhere in the
/// system. The ONE place a conversion may ever happen is here, and only where an
/// administrator has deliberately configured it, for a NAMED operation - so the
/// rule is scoped to (item + operation), and a conversion is applied only by a
/// caller that explicitly asks this rule for it (see <see cref="Convert"/>).
///
/// A rule is therefore never inferred from the item's units, its category, its
/// name or a global constant, and an operation with no configured rule converts
/// nothing (it refuses instead).
/// </summary>
public class ItemUnitConversion : AuditableEntity
{
    public Guid ItemId { get; private set; }

    /// <summary>The unit the amount is expressed in when the rule is applied.</summary>
    public UnitOfMeasure FromUnit { get; private set; }

    /// <summary>The unit the amount becomes after applying <see cref="Factor"/>.</summary>
    public UnitOfMeasure ToUnit { get; private set; }

    /// <summary>1 <see cref="FromUnit"/> = <see cref="Factor"/> <see cref="ToUnit"/>. Always &gt; 0.</summary>
    public decimal Factor { get; private set; }

    /// <summary>
    /// The operation this rule is scoped to (e.g. "Delivery", "Invoice"). Required and
    /// free-form on purpose: the phase never dictated a closed list of operations, so the
    /// factory names its own. Two rules for the same item+operation cannot coexist
    /// (unique index), and a rule for a different operation is never reused.
    /// </summary>
    public string Operation { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public string? Notes { get; private set; }

    private ItemUnitConversion() { } // EF Core

    public ItemUnitConversion(
        Guid itemId, UnitOfMeasure fromUnit, UnitOfMeasure toUnit, decimal factor,
        string operation, string createdBy, string? notes = null)
    {
        if (itemId == Guid.Empty)
            throw new DomainException("A unit conversion rule must name the item it belongs to.");
        if (fromUnit == toUnit)
            throw new DomainException(
                $"A unit conversion rule must convert between two DIFFERENT units - '{UnitName(fromUnit)}' to " +
                $"'{UnitName(toUnit)}' is not a conversion. KG and Meter are independent measurements.");
        if (factor <= 0)
            throw new DomainException("A unit conversion factor must be greater than zero.");
        if (string.IsNullOrWhiteSpace(operation))
            throw new DomainException("A unit conversion rule must name the operation it is scoped to.");

        ItemId = itemId;
        FromUnit = fromUnit;
        ToUnit = toUnit;
        Factor = factor;
        Operation = operation.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Re-configures an existing rule in place (upsert path).</summary>
    public void Update(UnitOfMeasure fromUnit, UnitOfMeasure toUnit, decimal factor, string? notes, string modifiedBy)
    {
        if (fromUnit == toUnit)
            throw new DomainException(
                $"A unit conversion rule must convert between two DIFFERENT units - '{UnitName(fromUnit)}' to " +
                $"'{UnitName(toUnit)}' is not a conversion. KG and Meter are independent measurements.");
        if (factor <= 0)
            throw new DomainException("A unit conversion factor must be greater than zero.");

        FromUnit = fromUnit;
        ToUnit = toUnit;
        Factor = factor;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    /// <summary>
    /// Applies THIS rule to an amount in <see cref="FromUnit"/>. Deliberately an instance
    /// method on the rule (not a static helper on the item) so a conversion can only ever
    /// run through a rule an administrator actually configured for this item and operation.
    /// </summary>
    public decimal Convert(decimal amountInFromUnit) => amountInFromUnit * Factor;

    /// <summary>
    /// True when this rule belongs to <paramref name="operation"/> and can convert
    /// <paramref name="fromUnit"/> into <paramref name="toUnit"/>. Callers use this to pick a
    /// rule explicitly; nothing applies a rule implicitly.
    /// </summary>
    public bool AppliesTo(string operation, UnitOfMeasure fromUnit, UnitOfMeasure toUnit)
        => IsActive
           && string.Equals(Operation, operation, StringComparison.OrdinalIgnoreCase)
           && FromUnit == fromUnit
           && ToUnit == toUnit;

    private static string UnitName(UnitOfMeasure unit) => unit == UnitOfMeasure.KG ? "KG" : "Meter";

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
