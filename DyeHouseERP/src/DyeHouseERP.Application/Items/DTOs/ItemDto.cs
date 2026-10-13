using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Items.DTOs;

/// <summary>
/// Item master DTO (spec sections 5-6 and 3). Carries both names so the UI can
/// render in Arabic or English; <see cref="Name"/> stays as a single
/// canonical name for documents and reports.
///
/// <see cref="BaseUnit"/> is the required primary unit. <see cref="SecondaryUnit"/>
/// is an optional second unit and, when present, differs from the primary one.
/// <see cref="RequireBothUnits"/> says whether a movement must record both
/// quantities. There is no automatic conversion between KG and Meter and no
/// "Top/توب" unit.
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
    public UnitOfMeasure? SecondaryUnit { get; set; }
    public bool RequireBothUnits { get; set; }
    public bool IsActive { get; set; }
}
