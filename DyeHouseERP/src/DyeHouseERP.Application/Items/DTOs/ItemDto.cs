using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Items.DTOs;

/// <summary>
/// Item master DTO (spec sections 6 and 3). Carries both names so the UI can
/// render in Arabic or English; <see cref="Name"/> stays as a single
/// canonical name for documents and reports.
/// BaseUnit is always exactly one of KG or Meter - there is no automatic
/// conversion between them and no "Top/توب" unit.
/// </summary>
public class ItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Category { get; set; }
    public UnitOfMeasure BaseUnit { get; set; }
    public bool IsActive { get; set; }
}
