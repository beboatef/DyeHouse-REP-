using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A purchase unit of measure (spec section 44).
///
/// Units are DATA here, not a fixed enum: the factory's default list is seeded
/// on first run, and authorized users can add their own bilingual units. A unit
/// that has ever been used is never hard deleted - it is DEACTIVATED, so every
/// historical purchase document keeps a resolvable name.
///
/// Three concepts are kept deliberately separate, exactly as spec section 44
/// requires:
///   * Purchase unit  - how the item is bought (وحدة، عدد، قطعة، كرتونة، ...)
///   * Inventory unit - how it is stocked (an item's own KG/Meter; a material's KG/Gram/Liter)
///   * Conversion     - OPTIONAL and explicit. Nothing is ever converted unless
///                      the user actually configured a factor here. 10 cartons
///                      x 20 pieces does NOT become 200 pieces on its own.
/// </summary>
public class PurchaseUnit : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// True for the units the system seeds on first run. They can be renamed and
    /// deactivated like any other unit, but they can never be removed, so the
    /// default list from spec section 44 is always present.
    /// </summary>
    public bool IsSystemDefault { get; private set; }

    /// <summary>
    /// OPTIONAL explicit conversion factor (spec section 44), e.g. 1 كرتونة = 20 قطعة.
    /// Null means "no conversion is defined" - which is the default, because the
    /// system never guesses a conversion.
    /// </summary>
    public decimal? ConversionFactor { get; private set; }

    /// <summary>The unit a <see cref="ConversionFactor"/> converts into. Always set together with the factor.</summary>
    public Guid? BaseUnitId { get; private set; }

    public string? Notes { get; private set; }

    private PurchaseUnit() { } // EF Core

    public PurchaseUnit(
        string code, string nameAr, string nameEn, string createdBy,
        decimal? conversionFactor = null, Guid? baseUnitId = null,
        string? notes = null, bool isSystemDefault = false)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        // Id is already assigned by BaseEntity before this constructor body runs,
        // so the self-reference check can use the real value.
        SetConversion(conversionFactor, baseUnitId, Id);
        Notes = Normalize(notes);
        IsSystemDefault = isSystemDefault;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Renames a unit and re-configures its conversion. The code is intentionally
    /// NOT editable: purchase documents and imports reference units by code.
    /// </summary>
    public void Update(
        string nameAr, string nameEn, decimal? conversionFactor, Guid? baseUnitId,
        string? notes, string modifiedBy)
    {
        SetNames(nameAr, nameEn);
        SetConversion(conversionFactor, baseUnitId, Id);
        Notes = Normalize(notes);
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Deactivates a unit instead of deleting it, so used units stay resolvable (spec section 44).</summary>
    public void Deactivate(string modifiedBy)
    {
        IsActive = false;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Activate(string modifiedBy)
    {
        IsActive = true;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A purchase unit code is required.");
        Code = code.Trim().ToUpperInvariant();
    }

    private void SetNames(string nameAr, string nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr))
            throw new DomainException("A purchase unit requires an Arabic name.");
        if (string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A purchase unit requires an English name.");

        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
    }

    private void SetConversion(decimal? conversionFactor, Guid? baseUnitId, Guid selfId)
    {
        // The two halves of a conversion are one decision: a factor with no target
        // unit is as meaningless as a target unit with no factor, so neither may
        // exist without the other.
        if (conversionFactor.HasValue && !baseUnitId.HasValue)
            throw new DomainException("A conversion factor requires the unit it converts into.");
        if (!conversionFactor.HasValue && baseUnitId.HasValue)
            throw new DomainException("A conversion target unit requires an explicit conversion factor.");

        if (conversionFactor is <= 0)
            throw new DomainException("A conversion factor must be greater than zero.");

        if (baseUnitId.HasValue && baseUnitId.Value == selfId)
            throw new DomainException("A unit cannot be defined as converting into itself.");

        ConversionFactor = conversionFactor;
        BaseUnitId = baseUnitId;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
