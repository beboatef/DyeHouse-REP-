namespace DyeHouseERP.Application.PurchaseUnits.DTOs;

public class PurchaseUnitDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>One of the nine seeded defaults (spec section 44) - renameable and deactivatable, never removable.</summary>
    public bool IsSystemDefault { get; set; }

    /// <summary>Null means no conversion is defined for this unit, which is the normal case.</summary>
    public decimal? ConversionFactor { get; set; }
    public Guid? BaseUnitId { get; set; }
    public string? BaseUnitName { get; set; }
    public string? BaseUnitNameAr { get; set; }
    public string? Notes { get; set; }
}
