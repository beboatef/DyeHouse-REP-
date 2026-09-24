using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A raw material movement OUT of normal internal production for a reason
/// other than consumption by a Production Order (spec section 14): return
/// to the customer, sending to an external processor, or an outright sale
/// of raw material. Always references the specific source message the
/// quantity is drawn from - never auto-selected.
/// </summary>
public class RawExternalRelease : AuditableEntity
{
    public string ReleaseNumber { get; private set; } = string.Empty; // system-generated, e.g. "REL-2026-000042"
    public DateTime ReleaseDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid RawMessageId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public RawReleaseReason Reason { get; private set; }

    /// <summary>The external party involved, if any - e.g. the vendor for ExternalProcessing, or the buyer for Sale. Not applicable for ReturnToCustomer (the customer already IS the party).</summary>
    public string? ExternalParty { get; private set; }
    public string? Notes { get; private set; }

    public bool ApprovalRequired { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    private RawExternalRelease() { } // EF Core

    public RawExternalRelease(
        string releaseNumber, DateTime releaseDate, Guid customerId, Guid itemId, Guid rawMessageId,
        decimal? quantityKg, decimal? quantityMeter, RawReleaseReason reason, string createdBy,
        string? externalParty = null, string? notes = null, bool approvalRequired = false, string? approvedBy = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A raw external release must specify a KG and/or Meter quantity.");
        if (approvalRequired && string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("This release requires approval - provide an approver.", nameof(approvedBy));

        ReleaseNumber = releaseNumber;
        ReleaseDate = releaseDate;
        CustomerId = customerId;
        ItemId = itemId;
        RawMessageId = rawMessageId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        Reason = reason;
        ExternalParty = externalParty;
        Notes = notes;
        ApprovalRequired = approvalRequired;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = approvalRequired ? DateTime.UtcNow : null;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
