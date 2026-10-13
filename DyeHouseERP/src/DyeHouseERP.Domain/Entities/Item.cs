using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Item master data (spec sections 5-6).
///
/// The item defines its units EXPLICITLY and structurally - never in Excel notes
/// or free-text remarks:
///   * <see cref="BaseUnit"/> is the required primary unit (KG or Meter);
///   * <see cref="SecondaryUnit"/> is the optional second unit, and must differ
///     from the primary one;
///   * <see cref="RequireBothUnits"/> is a per-item setting that decides whether
///     a movement must record BOTH measurements or just one.
///
/// The two quantities describe INDEPENDENT measurements of the same stock and are
/// never derived one from the other - there is deliberately no KG&lt;-&gt;Meter
/// conversion anywhere in the system. There is also deliberately NO "Raw Type"
/// field and no "Top/توب" unit: roll/piece counts, when needed, are descriptive
/// data on the receiving documents, never an inventory unit.
///
/// Names are kept bilingually (NameAr/NameEn) because every user-facing surface
/// must work in Arabic and English (spec section 3). <see cref="Name"/> is retained
/// as the canonical single display name for existing report and document code
/// paths, and always mirrors NameEn (falling back to NameAr).
/// </summary>
public class Item : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Category { get; private set; }

    /// <summary>The required primary/base unit (KG or Meter).</summary>
    public UnitOfMeasure BaseUnit { get; private set; }

    /// <summary>
    /// The optional secondary unit. Null means the item uses a single unit only
    /// (the <see cref="BaseUnit"/>). When set it is always different from the base unit.
    /// </summary>
    public UnitOfMeasure? SecondaryUnit { get; private set; }

    /// <summary>
    /// Whether every movement for this item must record BOTH units rather than one.
    /// Defaults to false, so existing items keep their single-measurement behavior.
    /// May only be true while a <see cref="SecondaryUnit"/> is configured.
    /// </summary>
    public bool RequireBothUnits { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>The units this item actually uses (primary, plus secondary when configured).</summary>
    public IReadOnlyList<UnitOfMeasure> ConfiguredUnits => ItemUnitRules.ConfiguredUnits(BaseUnit, SecondaryUnit);

    /// <summary>Whether this item is allowed to post a quantity in the given unit.</summary>
    public bool UsesUnit(UnitOfMeasure unit) => ConfiguredUnits.Contains(unit);

    private Item() { } // EF Core

    public Item(string code, string name, UnitOfMeasure baseUnit, string createdBy,
        string? nameAr = null, string? nameEn = null, string? category = null,
        UnitOfMeasure? secondaryUnit = null, bool requireBothUnits = false)
    {
        SetCode(code);
        SetNames(name, nameAr, nameEn);
        SetCategory(category);

        ItemUnitRules.EnsureValidConfiguration(baseUnit, secondaryUnit, requireBothUnits, Code);
        BaseUnit = baseUnit;
        SecondaryUnit = secondaryUnit;
        RequireBothUnits = requireBothUnits && secondaryUnit.HasValue;

        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Item code is required.", nameof(code));
        Code = code.Trim();
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        Name = name.Trim();
        if (string.IsNullOrWhiteSpace(NameEn)) NameEn = Name;
        if (string.IsNullOrWhiteSpace(NameAr)) NameAr = Name;
    }

    /// <summary>
    /// Sets the bilingual name pair (spec section 3). Either side may be
    /// omitted on input, in which case the other side is used as the fallback -
    /// an import that only carries one language still produces a usable item.
    /// </summary>
    public void SetNames(string? nameAr, string? nameEn) => SetNames(null, nameAr, nameEn);

    public void SetNames(string? name, string? nameAr, string? nameEn)
    {
        var en = FirstNonEmpty(nameEn, name, nameAr);
        var ar = FirstNonEmpty(nameAr, name, nameEn);

        if (string.IsNullOrWhiteSpace(ar) && string.IsNullOrWhiteSpace(en))
            throw new ArgumentException("Item name is required.", nameof(nameEn));

        NameEn = en is null ? string.Empty : en.Trim();
        NameAr = ar is null ? string.Empty : ar.Trim();
        Name = NameEn.Length > 0 ? NameEn : NameAr;
    }

    public void SetCategory(string? category) => Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    /// <summary>
    /// Changes the base unit (KG &lt;-&gt; Meter). Callers MUST first verify the item has
    /// no inventory history: reinterpreting already-recorded quantities is exactly
    /// what spec section 53 forbids. The RuleFor is enforced in UpdateItemCommandHandler.
    /// </summary>
    public void ChangeBaseUnit(UnitOfMeasure baseUnit, string modifiedBy)
    {
        ItemUnitRules.EnsureValidConfiguration(baseUnit, SecondaryUnit, RequireBothUnits, Code);
        BaseUnit = baseUnit;
        Touch(modifiedBy);
    }

    /// <summary>
    /// Sets the whole unit configuration in one validated step (primary unit,
    /// optional secondary unit, and whether both quantities are required).
    /// Callers MUST verify the item has no inventory history before letting this
    /// change the primary/secondary unit - see UpdateItemCommandHandler.
    /// </summary>
    public void ConfigureUnits(
        UnitOfMeasure baseUnit, UnitOfMeasure? secondaryUnit, bool requireBothUnits, string modifiedBy)
    {
        ItemUnitRules.EnsureValidConfiguration(baseUnit, secondaryUnit, requireBothUnits, Code);

        BaseUnit = baseUnit;
        SecondaryUnit = secondaryUnit;
        RequireBothUnits = requireBothUnits && secondaryUnit.HasValue;
        Touch(modifiedBy);
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
