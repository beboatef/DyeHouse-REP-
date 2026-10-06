using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// The CUSTOMER SERVICE PRICE LIST (spec section 36): stage + customer + unit ->
/// the price the customer is charged for that service.
///
/// <see cref="CustomerId"/> is NULL for the GENERAL DEFAULT price, and set for a
/// customer-specific override. Resolution is always specific-first, then general
/// (see ApplyServicePriceToJobOrderCommand); a Job Order never re-reads this table
/// once its price has been snapshotted.
/// </summary>
public class CustomerServicePrice : AuditableEntity
{
    public Guid StageDefinitionId { get; private set; }

    /// <summary>Null = the general/default price. Set = that customer's own price.</summary>
    public Guid? CustomerId { get; private set; }

    public UnitOfMeasure Unit { get; private set; }

    /// <summary>Selling price per unit for this stage. Never negative.</summary>
    public decimal PricePerUnit { get; private set; }

    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    /// <summary>True when this row is the general default rather than a customer override.</summary>
    public bool IsGeneralDefault => CustomerId is null;

    private CustomerServicePrice() { } // EF Core

    public CustomerServicePrice(
        Guid stageDefinitionId, Guid? customerId, UnitOfMeasure unit,
        decimal pricePerUnit, string createdBy, string? notes = null)
    {
        if (stageDefinitionId == Guid.Empty)
            throw new DomainException("A service price must name the production stage it applies to.");
        if (pricePerUnit < 0)
            throw new DomainException("A service price cannot be negative.");

        StageDefinitionId = stageDefinitionId;
        CustomerId = customerId;
        Unit = unit;
        PricePerUnit = pricePerUnit;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(decimal pricePerUnit, string? notes, string modifiedBy)
    {
        if (pricePerUnit < 0)
            throw new DomainException("A service price cannot be negative.");

        PricePerUnit = pricePerUnit;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Deactivated rather than deleted. Note what this does NOT do: it never
    /// changes the price of a Job Order that already snapshotted it (spec section
    /// 34) - the snapshot is the Job Order's own stored figure.
    /// </summary>
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
