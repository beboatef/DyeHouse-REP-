using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Item master data - deliberately minimal per spec section 8.
/// BaseUnit is EITHER KG OR Meter, never both, and never auto-converted.
/// </summary>
public class Item : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public UnitOfMeasure BaseUnit { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Item() { } // EF Core

    public Item(string code, string name, UnitOfMeasure baseUnit, string createdBy)
    {
        SetCode(code);
        SetName(name);
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
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
