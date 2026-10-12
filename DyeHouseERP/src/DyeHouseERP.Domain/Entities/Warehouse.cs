using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Generic warehouse master. Used for raw material warehouses, material
/// warehouses (spec section 25) and the ready goods warehouse (spec section
/// 29). Kept generic and configurable - the count/type of warehouses is
/// never hard-coded.
/// </summary>
public class Warehouse : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public WarehouseKind Kind { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Warehouse() { } // EF Core

    public Warehouse(string code, string name, WarehouseKind kind, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Warehouse code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Warehouse name is required.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        Kind = kind;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Renames the warehouse (spec section 15). The Code is the stable identity every
    /// document, ledger row and report already refers to, so it is deliberately never
    /// editable - only the human-facing name is.
    /// </summary>
    public void SetName(string name, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Warehouse name is required.", nameof(name));

        Name = name.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Changes the warehouse's kind. This is a classification change only: no stock is
    /// moved, no ledger row is written and no existing document loses its warehouse
    /// reference. Which kind a NEW document may use is enforced where that document is
    /// created (for example a ready-goods transfer requires a ReadyGoods warehouse).
    /// </summary>
    public void SetKind(WarehouseKind kind, string modifiedBy)
    {
        if (!Enum.IsDefined(typeof(WarehouseKind), kind))
            throw new ArgumentException("Unknown warehouse kind.", nameof(kind));

        Kind = kind;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Takes the warehouse out of use for NEW documents. Nothing is ever deleted: the row
    /// stays, its balances and ledger rows stay readable, and every document that already
    /// points at it keeps its reference - deactivation is a flag, never a removal
    /// (spec sections 15 and 42).
    /// </summary>
    public void Deactivate(string modifiedBy)
    {
        IsActive = false;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Puts a deactivated warehouse back into use for new documents.</summary>
    public void Activate(string modifiedBy)
    {
        IsActive = true;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Warehouse kinds (spec section 15). The structure stays configurable -
/// an admin can add as many warehouses of any kind as the factory needs;
/// these are only the defaults that make the two ownership worlds explicit:
/// RawMaterial / ProductionWip / ReadyGoods hold CUSTOMER-owned material,
/// Materials / OperatingSupplies hold FACTORY-owned stock.
/// </summary>
public enum WarehouseKind
{
    RawMaterial = 1,
    Materials = 2,
    ReadyGoods = 3,

    /// <summary>Production / WIP (spec section 15) - customer-owned material currently being processed.</summary>
    ProductionWip = 4,

    /// <summary>Operating supplies: spare parts, winding supplies, packaging, maintenance and consumption items (spec section 27) - never charged to a Job Order automatically.</summary>
    OperatingSupplies = 5
}
