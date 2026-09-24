using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Reprocessable material split off during a production stage (spec section
/// 22) - explicitly NOT the same as loss/waste. Tracked from the moment a
/// stage records a Separates quantity through to either being reprocessed
/// (a brand new Production Order - the original order's history is never
/// touched, spec section 23) or formally declared Scrapped.
/// </summary>
public class Separate : AuditableEntity
{
    public Guid OriginalProductionOrderId { get; private set; }
    public Guid StageExecutionId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public string? Reason { get; private set; }
    public string? Notes { get; private set; }
    public SeparateStatus Status { get; private set; } = SeparateStatus.PendingReprocessing;

    /// <summary>Set once a reprocessing order has been created from this separate (spec section 23).</summary>
    public Guid? ReprocessingProductionOrderId { get; private set; }

    private Separate() { } // EF Core

    public Separate(
        Guid originalProductionOrderId, Guid stageExecutionId, Guid customerId, Guid itemId,
        decimal? quantityKg, decimal? quantityMeter, string createdBy, string? reason = null, string? notes = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A separate must specify a KG and/or Meter quantity.");

        OriginalProductionOrderId = originalProductionOrderId;
        StageExecutionId = stageExecutionId;
        CustomerId = customerId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        Reason = reason;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void StartReprocessing(Guid reprocessingProductionOrderId)
    {
        if (Status != SeparateStatus.PendingReprocessing)
            throw new DomainException($"Separate is {Status} and cannot start reprocessing again.");

        Status = SeparateStatus.Reprocessing;
        ReprocessingProductionOrderId = reprocessingProductionOrderId;
    }

    public void MarkReprocessed()
    {
        if (Status != SeparateStatus.Reprocessing)
            throw new DomainException($"Separate is {Status}, expected Reprocessing.");

        Status = SeparateStatus.Reprocessed;
    }

    public void Scrap(string reason, string modifiedBy)
    {
        if (Status is SeparateStatus.Reprocessed or SeparateStatus.Scrapped)
            throw new DomainException($"Separate is already {Status} and cannot be scrapped.");

        Status = SeparateStatus.Scrapped;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Scrapped by {modifiedBy}] {reason}" : $"{Notes}\n[Scrapped by {modifiedBy}] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
