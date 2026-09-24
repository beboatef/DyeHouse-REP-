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

public enum WarehouseKind
{
    RawMaterial = 1,
    Materials = 2,
    ReadyGoods = 3
}
