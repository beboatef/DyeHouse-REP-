using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>Materials/chemicals master data (spec section 24) - kept separate from the fabric Item master.</summary>
public class Material : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public MaterialUnit Unit { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Material() { } // EF Core

    public Material(string code, string name, MaterialUnit unit, decimal purchasePrice, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Material code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Material name is required.", nameof(name));
        if (purchasePrice < 0) throw new ArgumentException("Purchase price cannot be negative.", nameof(purchasePrice));

        Code = code.Trim();
        Name = name.Trim();
        Unit = unit;
        PurchasePrice = purchasePrice;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}
