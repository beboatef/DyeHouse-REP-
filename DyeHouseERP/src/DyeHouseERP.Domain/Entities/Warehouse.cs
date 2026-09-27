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
        Code = code.Trim();
        Name = name.Trim();
        Kind = kind;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
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
