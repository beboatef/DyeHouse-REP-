using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Items.DTOs;

/// <summary>
/// An explicit, item-scoped unit conversion rule (spec sections 5-6): 1 <see cref="FromUnit"/>
/// = <see cref="Factor"/> <see cref="ToUnit"/>, for the named <see cref="Operation"/> only.
/// There is no global KG&lt;-&gt;Meter conversion; this DTO represents the ONLY kind that exists.
/// </summary>
public class ItemUnitConversionDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public UnitOfMeasure FromUnit { get; set; }
    public UnitOfMeasure ToUnit { get; set; }
    public decimal Factor { get; set; }
    public string Operation { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}
