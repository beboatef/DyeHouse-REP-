using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>Material consumption against a Production Order (spec section 27) - the actual cost driver for that order's material cost.</summary>
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
}
