using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// The SNAPSHOT of the price a Job Order is charged (spec section 34, as confirmed
/// with the business): ONE row per Job Order per unit.
///
/// This is the record that makes historical Job Orders immune to later price-list
/// edits. Once a price is applied here, changing or deactivating the
/// <see cref="CustomerServicePrice"/> row it came from has NO effect on this order -
/// the number lives on the order, not by reference to a mutable list.
///
/// Re-applying a price is a deliberate, audited act (see the Apply command): it
/// stores the superseded figure in <see cref="PreviousPricePerUnit"/>, requires a
/// reason when it is a manual override, and records who and when. It is never a
/// side effect of editing the price list.
/// </summary>
public class ProductionOrderServicePrice : AuditableEntity
{
    public Guid ProductionOrderId { get; private set; }

    /// <summary>The stage/service this price is for - explicitly chosen by the user.</summary>
    public Guid StageDefinitionId { get; private set; }

    public UnitOfMeasure Unit { get; private set; }

    /// <summary>The snapshotted selling price per unit. This is what revenue is computed from.</summary>
    public decimal PricePerUnit { get; private set; }

    /// <summary>The customer whose specific price list row supplied this, or null when the general default was used.</summary>
    public Guid? SourceCustomerId { get; private set; }

    /// <summary>True when an authorized user typed the price directly instead of taking it from the list.</summary>
    public bool IsOverride { get; private set; }

    /// <summary>The figure this row replaced, when it has been re-applied. Null on first application.</summary>
    public decimal? PreviousPricePerUnit { get; private set; }

    /// <summary>Required for a manual override; optional when the price came from the list.</summary>
    public string? OverrideReason { get; private set; }

    public DateTime PricedAtUtc { get; private set; }
    public string PricedBy { get; private set; } = string.Empty;

    private ProductionOrderServicePrice() { } // EF Core

    public ProductionOrderServicePrice(
        Guid productionOrderId, Guid stageDefinitionId, UnitOfMeasure unit,
        decimal pricePerUnit, Guid? sourceCustomerId, bool isOverride,
        string? overrideReason, string pricedBy)
    {
        if (productionOrderId == Guid.Empty)
            throw new DomainException("A service price snapshot must belong to a Job Order.");
        if (stageDefinitionId == Guid.Empty)
            throw new DomainException("A service price snapshot must name the stage it applies to.");
        if (pricePerUnit < 0)
            throw new DomainException("A service price cannot be negative.");
        if (isOverride && string.IsNullOrWhiteSpace(overrideReason))
            throw new DomainException("A manual price override requires a reason.");

        ProductionOrderId = productionOrderId;
        StageDefinitionId = stageDefinitionId;
        Unit = unit;
        PricePerUnit = pricePerUnit;
        SourceCustomerId = sourceCustomerId;
        IsOverride = isOverride;
        OverrideReason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        PricedBy = pricedBy;
        PricedAtUtc = DateTime.UtcNow;
        CreatedBy = pricedBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-applies a price to the same Job Order/unit. The superseded figure is kept
    /// on the row so the change is visible without reading the audit log, and the
    /// reason is mandatory for another manual override.
    /// </summary>
    public void Reapply(
        Guid stageDefinitionId, decimal pricePerUnit, Guid? sourceCustomerId, bool isOverride,
        string? overrideReason, string modifiedBy)
    {
        if (stageDefinitionId == Guid.Empty)
            throw new DomainException("A service price snapshot must name the stage it applies to.");
        if (pricePerUnit < 0)
            throw new DomainException("A service price cannot be negative.");
        if (isOverride && string.IsNullOrWhiteSpace(overrideReason))
            throw new DomainException("A manual price override requires a reason.");

        StageDefinitionId = stageDefinitionId;
        PreviousPricePerUnit = PricePerUnit;
        PricePerUnit = pricePerUnit;
        SourceCustomerId = sourceCustomerId;
        IsOverride = isOverride;
        OverrideReason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        PricedBy = modifiedBy;
        PricedAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
