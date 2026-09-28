using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// An internal issue of OPERATING SUPPLIES (spec section 27): spare parts,
/// winding/packaging consumables, maintenance supplies, etc. handed to a
/// department, maintenance or another internal user.
///
/// Deliberately separate from MaterialIssue: a MaterialIssue is charged to a
/// Job Order and becomes part of that order's cost, while a supply issue is
/// NOT automatically charged to any Job Order - it is internal consumption
/// recorded against a department/purpose. Both move the same Material ledger,
/// so stock never diverges between the two.
///
/// Posting is the only step with a stock effect: one OUT MaterialTransaction
/// per line. Cancelling posts an equal-and-opposite IN row instead of
/// deleting history (spec section 10).
/// </summary>
public class SupplyIssue : AuditableEntity
{
    public string IssueNumber { get; private set; } = string.Empty; // system-generated, e.g. "SUP-2026-000014"
    public DateTime IssueDate { get; private set; }
    public Guid WarehouseId { get; private set; }

    /// <summary>Optional link to the payroll department master; the free-text name below is always kept as the snapshot.</summary>
    public Guid? DepartmentId { get; private set; }
    public string IssuedTo { get; private set; } = string.Empty;   // department / maintenance / production / other internal user
    public string Purpose { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public SupplyIssueStatus Status { get; private set; } = SupplyIssueStatus.Draft;

    public string? PostedBy { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<SupplyIssueLine> _lines = new();
    public IReadOnlyCollection<SupplyIssueLine> Lines => _lines.AsReadOnly();

    private SupplyIssue() { } // EF Core

    public SupplyIssue(string issueNumber, DateTime issueDate, Guid warehouseId, string issuedTo, string purpose,
        string createdBy, Guid? departmentId = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(issueNumber)) throw new ArgumentException("Issue number is required.", nameof(issueNumber));
        if (string.IsNullOrWhiteSpace(issuedTo)) throw new ArgumentException("The internal recipient is required.", nameof(issuedTo));
        if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Purpose is required.", nameof(purpose));

        IssueNumber = issueNumber;
        IssueDate = issueDate;
        WarehouseId = warehouseId;
        IssuedTo = issuedTo.Trim();
        Purpose = purpose.Trim();
        DepartmentId = departmentId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status == SupplyIssueStatus.Draft;
    public decimal TotalCost => _lines.Sum(l => l.TotalCost);

    public SupplyIssueLine AddLine(Guid materialId, decimal quantity, MaterialUnit unit, decimal unitCost, string? notes = null)
    {
        EnsureEditable();
        var line = new SupplyIssueLine(Id, materialId, quantity, unit, unitCost, notes);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new DomainException("That line does not belong to this supply issue.");
        _lines.Remove(line);
    }

    public void Post(string modifiedBy)
    {
        if (Status == SupplyIssueStatus.Posted) throw new DomainException("This supply issue has already been posted.");
        EnsureNotCancelled();
        if (_lines.Count == 0) throw new DomainException("A supply issue must have at least one line before it can be posted.");

        Status = SupplyIssueStatus.Posted;
        PostedBy = modifiedBy;
        PostedAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == SupplyIssueStatus.Cancelled) throw new DomainException("This supply issue is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        Status = SupplyIssueStatus.Cancelled;
        CancelledBy = modifiedBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
        Lock();
    }

    private void EnsureEditable()
    {
        EnsureNotCancelled();
        if (Status != SupplyIssueStatus.Draft)
            throw new DocumentLockedException("Supply Issue", IssueNumber);
    }

    private void EnsureNotCancelled()
    {
        if (Status == SupplyIssueStatus.Cancelled)
            throw new DomainException("A cancelled supply issue cannot be changed.");
    }
}

public class SupplyIssueLine : BaseEntity
{
    public Guid SupplyIssueId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public MaterialUnit Unit { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost => Quantity * UnitCost;
    public string? Notes { get; private set; }

    private SupplyIssueLine() { } // EF Core

    public SupplyIssueLine(Guid supplyIssueId, Guid materialId, decimal quantity, MaterialUnit unit, decimal unitCost, string? notes = null)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitCost < 0) throw new ArgumentException("Unit cost cannot be negative.", nameof(unitCost));

        SupplyIssueId = supplyIssueId;
        MaterialId = materialId;
        Quantity = quantity;
        Unit = unit;
        UnitCost = unitCost;
        Notes = notes;
    }
}
