using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Materials.DTOs;

public class MaterialDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MaterialUnit Unit { get; set; }
    public decimal PurchasePrice { get; set; }
    public bool IsActive { get; set; }
}
