using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A new processing/production request submitted by a customer through the
/// portal (spec section 36). The customer never gets direct access to
/// internal administration - this request enters the normal internal
/// workflow and staff either reject it or convert it into a real
/// Production Order.
/// </summary>
public class ProductionRequest : AuditableEntity
{
    public string RequestNumber { get; private set; } = string.Empty;
    public DateTime RequestDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Color { get; private set; }
    public decimal? RequestedQuantityKg { get; private set; }
    public decimal? RequestedQuantityMeter { get; private set; }
    public string? Notes { get; private set; }
    public ProductionRequestStatus Status { get; private set; } = ProductionRequestStatus.Pending;
    public Guid? ConvertedProductionOrderId { get; private set; }
    public string? StaffNotes { get; private set; }

    private ProductionRequest() { } // EF Core

    public ProductionRequest(string requestNumber, DateTime requestDate, Guid customerId, Guid itemId,
        string createdBy, string? color = null, decimal? requestedQuantityKg = null,
        decimal? requestedQuantityMeter = null, string? notes = null)
    {
        RequestNumber = requestNumber;
        RequestDate = requestDate;
        CustomerId = customerId;
        ItemId = itemId;
        Color = color;
        RequestedQuantityKg = requestedQuantityKg;
        RequestedQuantityMeter = requestedQuantityMeter;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Approve(string staffNotes, string modifiedBy)
    {
        if (Status != ProductionRequestStatus.Pending) throw new DomainException($"Request is {Status}, expected Pending.");
        Status = ProductionRequestStatus.Approved;
        StaffNotes = staffNotes;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string reason, string modifiedBy)
    {
        if (Status != ProductionRequestStatus.Pending) throw new DomainException($"Request is {Status}, expected Pending.");
        Status = ProductionRequestStatus.Rejected;
        StaffNotes = reason;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void MarkConverted(Guid productionOrderId, string modifiedBy)
    {
        if (Status != ProductionRequestStatus.Approved) throw new DomainException($"Request is {Status}, expected Approved.");
        Status = ProductionRequestStatus.ConvertedToOrder;
        ConvertedProductionOrderId = productionOrderId;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
