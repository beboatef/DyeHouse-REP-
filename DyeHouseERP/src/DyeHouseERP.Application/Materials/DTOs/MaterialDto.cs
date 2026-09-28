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

    /// <summary>Chemical (issued to a Job Order) vs OperatingSupply (issued to a department) - spec sections 23 + 27.</summary>
    public MaterialKind Kind { get; set; } = MaterialKind.Chemical;

    /// <summary>Optional low-stock threshold, nullable so "not tracked" is distinct from zero (spec section 51).</summary>
    public decimal? ReorderLevel { get; set; }

    /// <summary>Live on-hand quantity from the material ledger (spec section 10).</summary>
    public decimal CurrentBalance { get; set; }
}
