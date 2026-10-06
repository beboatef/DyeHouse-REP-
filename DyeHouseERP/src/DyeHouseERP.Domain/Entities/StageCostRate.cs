using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// The ACTUAL COST LIST (spec section 34A): the company's internal cost per unit
/// for a stage/service - stage + unit -> actual company cost.
///
/// This is deliberately NOT the customer price. Keeping the two apart is the whole
/// point of section 34: what a stage costs the factory and what the customer is
/// charged are different numbers, stored in different lists, and conflating them
/// would make real profitability impossible to compute.
/// </summary>
public class StageCostRate : AuditableEntity
{
    public Guid StageDefinitionId { get; private set; }
    public UnitOfMeasure Unit { get; private set; }

    /// <summary>Actual company cost for one unit of this stage. Never negative.</summary>
    public decimal CostPerUnit { get; private set; }

    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    private StageCostRate() { } // EF Core

    public StageCostRate(
        Guid stageDefinitionId, UnitOfMeasure unit, decimal costPerUnit, string createdBy, string? notes = null)
    {
        if (stageDefinitionId == Guid.Empty)
            throw new DomainException("A cost rate must name the production stage it applies to.");
        if (costPerUnit < 0)
            throw new DomainException("A stage cost rate cannot be negative.");

        StageDefinitionId = stageDefinitionId;
        Unit = unit;
        CostPerUnit = costPerUnit;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(decimal costPerUnit, string? notes, string modifiedBy)
    {
        if (costPerUnit < 0)
            throw new DomainException("A stage cost rate cannot be negative.");

        CostPerUnit = costPerUnit;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Deactivated rather than deleted - a rate that priced a Job Order must stay resolvable.</summary>
    public void Deactivate(string modifiedBy)
    {
        IsActive = false;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Activate(string modifiedBy)
    {
        IsActive = true;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
