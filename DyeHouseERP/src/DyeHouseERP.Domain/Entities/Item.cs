using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Item master data (spec section 6).
/// BaseUnit is EITHER KG OR Meter, never both, and never auto-converted -
/// there is deliberately no KG&lt;-&gt;Meter conversion anywhere in the system.
/// There is also deliberately NO "Raw Type" field and no "Top/توب" unit:
/// roll/piece counts, when needed, are descriptive data on the receiving
/// documents, never an inventory unit.
/// Names are kept bilingually (NameAr/NameEn) because every user-facing
/// surface must work in Arabic and English (spec section 3). <see cref="Name"/> 
/// is retained as the canonical single display name for existing report and
/// document code paths, and always mirrors NameEn (falling back to NameAr).
/// </summary>
public class Item : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public UnitOfMeasure BaseUnit { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Item() { } // EF Core

    public Item(string code, string name, UnitOfMeasure baseUnit, string createdBy,
        string? nameAr = null, string? nameEn = null, string? category = null)
    {
        SetCode(code);
        SetNames(name, nameAr, nameEn);
        SetCategory(category);
        BaseUnit = baseUnit;
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
        BaseUnit = baseUnit;
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
