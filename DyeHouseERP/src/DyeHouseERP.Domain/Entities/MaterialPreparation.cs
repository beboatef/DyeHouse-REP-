using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// "الإحلال" - preparing/diluting a material (spec section 28). NOT about
/// substituting one material for another; this records mixing an original
/// material with water/solvent into a resulting prepared solution, staying
/// traceable back to the original material and quantity consumed.
/// </summary>
public class MaterialPreparation : AuditableEntity
{
    public string PreparationNumber { get; private set; } = string.Empty;
    public DateTime PreparationDate { get; private set; }
    public Guid OriginalMaterialId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal OriginalQuantity { get; private set; }
    public decimal WaterQuantity { get; private set; }
    public decimal ResultingQuantity { get; private set; }
    public decimal? Concentration { get; private set; }
    public decimal? Cost { get; private set; }
    public Guid? ProductionOrderId { get; private set; }
    public string? Notes { get; private set; }

    private MaterialPreparation() { } // EF Core

    public MaterialPreparation(
        string preparationNumber, DateTime preparationDate, Guid originalMaterialId, Guid warehouseId,
        decimal originalQuantity, decimal waterQuantity, decimal resultingQuantity, string createdBy,
        decimal? concentration = null, decimal? cost = null, Guid? productionOrderId = null, string? notes = null)
    {
        if (originalQuantity <= 0) throw new ArgumentException("Original quantity must be greater than zero.", nameof(originalQuantity));
        if (resultingQuantity <= 0) throw new ArgumentException("Resulting quantity must be greater than zero.", nameof(resultingQuantity));

        PreparationNumber = preparationNumber;
        PreparationDate = preparationDate;
        OriginalMaterialId = originalMaterialId;
        WarehouseId = warehouseId;
        OriginalQuantity = originalQuantity;
        WaterQuantity = waterQuantity;
        ResultingQuantity = resultingQuantity;
        Concentration = concentration;
        Cost = cost;
        ProductionOrderId = productionOrderId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
