using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.CostAccounting.DTOs;

public class CostEntryDto
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public CostCategory Category { get; set; }
    public decimal Amount { get; set; }
    public DateTime EntryDate { get; set; }
    public string? Description { get; set; }
}

/// <summary>Production Order cost rollup (spec section 35) - always computed live, never divides by zero.</summary>
public class ProductionOrderCostDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public decimal MaterialCost { get; set; }
    public decimal PreparationCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal ElectricityCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal? OutputKg { get; set; }
    public decimal? OutputMeter { get; set; }
    public decimal? CostPerKg { get; set; }
    public decimal? CostPerMeter { get; set; }
    public decimal ProcessingValue { get; set; }
    public decimal Profit { get; set; }
    public decimal? MarginPercent { get; set; }
}
