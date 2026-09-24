using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// An operating cost (labor, electricity, fuel, maintenance, other -
/// spec section 35) assigned to a Production Order. Material and
/// preparation costs are NOT recorded here - they're derived from
/// MaterialIssue/MaterialPreparation to avoid double counting in the
/// order's cost rollup.
/// </summary>
public class CostEntry : AuditableEntity
{
    public Guid ProductionOrderId { get; private set; }
    public CostCategory Category { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime EntryDate { get; private set; }
    public string? Description { get; private set; }

    private CostEntry() { } // EF Core

    public CostEntry(Guid productionOrderId, CostCategory category, decimal amount, DateTime entryDate, string createdBy, string? description = null)
    {
        if (amount < 0) throw new ArgumentException("Amount cannot be negative.", nameof(amount));

        ProductionOrderId = productionOrderId;
        Category = category;
        Amount = amount;
        EntryDate = entryDate;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
