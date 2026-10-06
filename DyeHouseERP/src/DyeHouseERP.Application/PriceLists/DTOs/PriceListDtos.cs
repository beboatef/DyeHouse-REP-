using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.PriceLists.DTOs;

/// <summary>Actual Cost List row (spec section 34A): stage + unit -> company cost.</summary>
public class StageCostRateDto
{
    public Guid Id { get; set; }
    public Guid StageDefinitionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public UnitOfMeasure Unit { get; set; }
    public decimal CostPerUnit { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Customer Service Price List row (spec section 36): stage + customer + unit ->
/// selling price. <see cref="IsGeneralDefault"/> marks the fallback row that
/// applies when a customer has no specific price.
/// </summary>
public class CustomerServicePriceDto
{
    public Guid Id { get; set; }
    public Guid StageDefinitionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public bool IsGeneralDefault { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public decimal PricePerUnit { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

/// <summary>The Job Order's own snapshotted price (spec section 34) - one per unit.</summary>
public class ProductionOrderServicePriceDto
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public Guid StageDefinitionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public UnitOfMeasure Unit { get; set; }

    /// <summary>The snapshotted selling price. Later price-list edits never change this.</summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>True when a user typed the price instead of taking it from the list.</summary>
    public bool IsOverride { get; set; }

    public Guid? SourceCustomerId { get; set; }
    public string? SourceCustomerName { get; set; }
    public decimal? PreviousPricePerUnit { get; set; }
    public string? OverrideReason { get; set; }
    public string PricedBy { get; set; } = string.Empty;
    public DateTime PricedAtUtc { get; set; }
}

/// <summary>
/// Job Order profitability (spec section 37).
///
/// Revenue is computed ONCE for the order - final actual quantity x the
/// snapshotted service price - never summed per stage, as confirmed with the
/// business. Cost comes from the existing live cost rollup, so the two figures are
/// the real ones and neither is a placeholder.
/// </summary>
public class ProductionOrderProfitabilityDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;

    /// <summary>The snapshotted prices (one per unit), if the order has been priced.</summary>
    public List<ProductionOrderServicePriceDto> ServicePrices { get; set; } = new();

    /// <summary>Ready-goods transferred quantity, else the last completed stage's output.</summary>
    public decimal FinalQuantityKg { get; set; }
    public decimal FinalQuantityMeter { get; set; }

    /// <summary>Where the final quantity came from, so the figure is never ambiguous.</summary>
    public string FinalQuantitySource { get; set; } = string.Empty;

    /// <summary>Sum of (final quantity x snapshotted price) across the order's units.</summary>
    public decimal Revenue { get; set; }

    /// <summary>True when the order has no snapshotted price at all - revenue cannot be computed yet.</summary>
    public bool HasServicePrice { get; set; }

    /// <summary>The live actual cost rollup (spec section 34).</summary>
    public decimal ActualCost { get; set; }

    /// <summary>Revenue - ActualCost. Only meaningful once a price exists.</summary>
    public decimal Profit { get; set; }

    /// <summary>Profit / Revenue x 100, or null when revenue is zero (never a divide-by-zero).</summary>
    public decimal? MarginPercent { get; set; }

    /// <summary>
    /// The LEGACY processing value carried by the old cost rollup (output quantity x
    /// the old cost-per-unit basis). It is NOT the invoiced amount and NOT the revenue
    /// basis above - it is surfaced only so the two revenue notions can be compared,
    /// and it is deliberately named so it cannot be mistaken for an invoice figure.
    /// </summary>
    public decimal LegacyProcessingValue { get; set; }
}
