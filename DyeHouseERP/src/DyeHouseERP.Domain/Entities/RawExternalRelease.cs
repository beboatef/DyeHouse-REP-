using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

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

    // ---- External Processing workflow (spec section 21) ----
    // Only populated when Reason is ExternalProcessing. The stage is free text
    // rather than a fixed enum so a dyehouse can name its own processes
    // (External Printing, External Finishing, External Dyeing, Other).
    public Guid? ProductionOrderId { get; private set; }
    public string? ExternalProcessingStage { get; private set; }
    public decimal? ExternalProcessingCost { get; private set; }
    public DateTime? ExpectedReturnDate { get; private set; }

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. A release can
    /// be returned, corrected or cancelled at the same moment; only the first
    /// commit is allowed to move the ledger.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    public DateTime? ActualReturnDate { get; private set; }
    public decimal? ReturnedQuantityKg { get; private set; }
    public decimal? ReturnedQuantityMeter { get; private set; }
    public string? ReturnedBy { get; private set; }
    public string? CancellationReason { get; private set; }
    public ExternalProcessingStatus Status { get; private set; } = ExternalProcessingStatus.NotApplicable;

    private RawExternalRelease() { } // EF Core

    public RawExternalRelease(
        string releaseNumber, DateTime releaseDate, Guid customerId, Guid itemId, Guid rawMessageId,
        decimal? quantityKg, decimal? quantityMeter, RawReleaseReason reason, string createdBy,
        string? externalParty = null, string? notes = null, bool approvalRequired = false, string? approvedBy = null,
        Guid? productionOrderId = null, string? externalProcessingStage = null,
        decimal? externalProcessingCost = null, DateTime? expectedReturnDate = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A raw external release must specify a KG and/or Meter quantity.");
        if (approvalRequired && string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("This release requires approval - provide an approver.", nameof(approvedBy));
        if (externalProcessingCost is < 0)
            throw new ArgumentException("External processing cost cannot be negative.", nameof(externalProcessingCost));

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
        ProductionOrderId = reason == RawReleaseReason.ExternalProcessing ? productionOrderId : null;
        ExternalProcessingStage = reason == RawReleaseReason.ExternalProcessing ? externalProcessingStage : null;
        ExternalProcessingCost = reason == RawReleaseReason.ExternalProcessing ? externalProcessingCost : null;
        ExpectedReturnDate = reason == RawReleaseReason.ExternalProcessing ? expectedReturnDate : null;
        Status = reason == RawReleaseReason.ExternalProcessing
            ? ExternalProcessingStatus.AwaitingReturn
            : ExternalProcessingStatus.NotApplicable;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Updates the external processor, stage, cost or expected return while the material is still out.</summary>
    public void SetExternalProcessingDetails(string? externalParty, string? stage, decimal? cost,
        DateTime? expectedReturnDate, string modifiedBy)
    {
        if (Reason != RawReleaseReason.ExternalProcessing)
            throw new DomainException("Only an external processing release carries processing details.");
        if (Status == ExternalProcessingStatus.Returned || Status == ExternalProcessingStatus.Cancelled)
            throw new DocumentLockedException("External Processing release", ReleaseNumber);
        if (cost is < 0) throw new ArgumentException("External processing cost cannot be negative.", nameof(cost));

        ExternalParty = externalParty;
        ExternalProcessingStage = stage;
        ExternalProcessingCost = cost;
        ExpectedReturnDate = expectedReturnDate;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records material coming back from the external processor. Returns are
    /// additive up to the quantity originally sent, so the same physical
    /// quantity can never be booked back twice (spec section 20).
    /// </summary>
    public void RecordReturn(decimal? returnedKg, decimal? returnedMeter, DateTime actualReturnDate, string modifiedBy)
    {
        if (Reason != RawReleaseReason.ExternalProcessing)
            throw new DomainException("Only an external processing release can be returned.");
        if (Status == ExternalProcessingStatus.Cancelled)
            throw new DomainException("A cancelled external processing release cannot receive a return.");
        if (returnedKg is null && returnedMeter is null)
            throw new ArgumentException("Specify the returned quantity in KG and/or Meter.");

        var newKg = (ReturnedQuantityKg ?? 0) + (returnedKg ?? 0);
        var newMeter = (ReturnedQuantityMeter ?? 0) + (returnedMeter ?? 0);

        if (QuantityKg is not null && newKg > QuantityKg.Value)
            throw new DomainException($"Returned quantity ({newKg:0.###} KG) exceeds the quantity sent out ({QuantityKg.Value:0.###} KG).");
        if (QuantityMeter is not null && newMeter > QuantityMeter.Value)
            throw new DomainException($"Returned quantity ({newMeter:0.###} m) exceeds the quantity sent out ({QuantityMeter.Value:0.###} m).");

        ReturnedQuantityKg = newKg;
        ReturnedQuantityMeter = newMeter;
        ActualReturnDate = actualReturnDate;
        ReturnedBy = modifiedBy;

        var kgDone = QuantityKg is null || newKg >= QuantityKg.Value;
        var meterDone = QuantityMeter is null || newMeter >= QuantityMeter.Value;
        Status = kgDone && meterDone ? ExternalProcessingStatus.Returned : ExternalProcessingStatus.PartiallyReturned;

        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void CancelExternalProcessing(string reason, string modifiedBy)
    {
        if (Reason != RawReleaseReason.ExternalProcessing)
            throw new DomainException("Only an external processing release can be cancelled this way.");
        if (Status == ExternalProcessingStatus.Cancelled)
            throw new DomainException("This external processing release is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        Status = ExternalProcessingStatus.Cancelled;
        CancellationReason = reason.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
