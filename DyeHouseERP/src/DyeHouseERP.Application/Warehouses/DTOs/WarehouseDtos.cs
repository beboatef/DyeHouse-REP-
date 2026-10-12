using DyeHouseERP.Domain.Entities;

namespace DyeHouseERP.Application.Warehouses.DTOs;

/// <summary>
/// Warehouse master data as the application layer sees it (spec section 15). The API
/// maps this to its own response record, so the wire format is unchanged.
/// </summary>
public class WarehouseMasterDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WarehouseKind Kind { get; set; }
    public bool IsActive { get; set; }
}
