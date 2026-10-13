using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Material consumption against a Production Order (spec section 27) - the actual cost driver for that
/// order's material cost. An issue is a stock posting the moment it is created (there is no draft step),
/// so it can never be edited or deleted: cancelling leaves the original OUT ledger row untouched and
/// posts an equal-and-opposite IN row as its reversal (spec section 18/43 rules applied to materials).
/// </summary>
public class MaterialIssue : AuditableEntity
{
    public string IssueNumber { get; private set; } = string.Empty;
    public DateTime IssueDate { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost => Quantity * UnitCost;
    public string? Notes { get; private set; }

    public MaterialIssueStatus Status { get; private set; } = MaterialIssueStatus.Posted;
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>A cancelled issue is excluded from the Production Order's material cost and cannot be cancelled again.</summary>
    public bool IsCancelled => Status == MaterialIssueStatus.Cancelled;

    private MaterialIssue() { } // EF Core

    public MaterialIssue(string issueNumber, DateTime issueDate, Guid materialId, Guid warehouseId,
        Guid productionOrderId, decimal quantity, decimal unitCost, string createdBy, string? notes = null)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("Unit cost cannot be negative.", nameof(unitCost));

        IssueNumber = issueNumber;
        IssueDate = issueDate;
        MaterialId = materialId;
        WarehouseId = warehouseId;
        ProductionOrderId = productionOrderId;
        Quantity = quantity;
        UnitCost = unitCost;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Cancels a posted material issue. The caller is responsible for writing the compensating IN ledger
    /// row; this only flips the document state so a second cancellation is refused.
    /// </summary>
    public void Cancel(string reason, string modifiedBy)
    {
        if (IsCancelled) throw new DomainException("This material issue is already cancelled.");

        Status = MaterialIssueStatus.Cancelled;
        CancelledBy = modifiedBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
