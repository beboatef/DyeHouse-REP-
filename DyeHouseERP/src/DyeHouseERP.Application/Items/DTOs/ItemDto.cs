using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Items.DTOs;

public class ItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UnitOfMeasure BaseUnit { get; set; }
    public bool IsActive { get; set; }
}
