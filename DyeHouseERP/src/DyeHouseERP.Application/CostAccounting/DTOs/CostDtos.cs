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

/// <summary>Production Order cost rollup (spec section 34) - always computed live, never divides by zero.</summary>
public class ProductionOrderCostDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public decimal MaterialCost { get; set; }
    public decimal PreparationCost { get; set; }

    /// <summary>Cost of material sent out for external processing (spec sections 21 + 34).</summary>
    public decimal ExternalProcessingCost { get; set; }

    public decimal LaborCost { get; set; }
    public decimal ElectricityCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }

    // ---- Additional cost lines (spec section 35) ----
    /// <summary>نقل - transport / freight.</summary>
    public decimal TransportCost { get; set; }

    /// <summary>تعبئة - packaging.</summary>
    public decimal PackagingCost { get; set; }

    /// <summary>إصلاح - repairs.</summary>
    public decimal RepairCost { get; set; }

    public decimal OtherCost { get; set; }

    /// <summary>ACTUAL cost: the live rollup of everything posted above.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>What the order was quoted/planned at - advisory, never a substitute for the actual.</summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>The frozen figure an authorized user signed off on, with who and when.</summary>
    public decimal? ApprovedCost { get; set; }
    public string? CostApprovedBy { get; set; }
    public DateTime? CostApprovedAtUtc { get; set; }
    public string? CostingNotes { get; set; }

    /// <summary>Approved - Actual. Positive means the job came in under the approved figure.</summary>
    public decimal? ApprovedVariance { get; set; }

    public decimal? OutputKg { get; set; }
    public decimal? OutputMeter { get; set; }
    public decimal? CostPerKg { get; set; }
    public decimal? CostPerMeter { get; set; }
    public decimal ProcessingValue { get; set; }
    public decimal Profit { get; set; }
    public decimal? MarginPercent { get; set; }
}
