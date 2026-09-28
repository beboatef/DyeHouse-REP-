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

    /// <summary>
    /// Chemical (production dye/auxiliary) or OperatingSupply (spare parts,
    /// packaging, maintenance consumables - spec section 27). Factory-owned
    /// in both cases, but a different store and a different issue workflow:
    /// chemicals are issued to Job Orders, supplies are issued to departments.
    /// </summary>
    public MaterialKind Kind { get; private set; } = MaterialKind.Chemical;

    /// <summary>
    /// Optional reorder threshold (spec section 51 "low stock"). When set, the
    /// dashboard flags the material once its live ledger balance drops below
    /// this figure. Null means "not tracked" - never treated as zero, which
    /// would flag every material in the catalogue.
    /// </summary>
    public decimal? ReorderLevel { get; private set; }

    public bool IsActive { get; private set; } = true;

    private Material() { } // EF Core

    public Material(string code, string name, MaterialUnit unit, decimal purchasePrice, string createdBy,
        MaterialKind kind = MaterialKind.Chemical)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Material code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Material name is required.", nameof(name));
        if (purchasePrice < 0) throw new ArgumentException("Purchase price cannot be negative.", nameof(purchasePrice));

        Code = code.Trim();
        Name = name.Trim();
        Unit = unit;
        PurchasePrice = purchasePrice;
        Kind = kind;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetName(string name, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Material name is required.", nameof(name));
        Name = name.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void SetPurchasePrice(decimal purchasePrice, string modifiedBy)
    {
        if (purchasePrice < 0) throw new ArgumentException("Purchase price cannot be negative.", nameof(purchasePrice));
        PurchasePrice = purchasePrice;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void SetKind(MaterialKind kind, string modifiedBy)
    {
        Kind = kind;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void SetReorderLevel(decimal? reorderLevel, string modifiedBy)
    {
        if (reorderLevel is < 0) throw new ArgumentException("Reorder level cannot be negative.", nameof(reorderLevel));
        ReorderLevel = reorderLevel;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}
