using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A manual, audited correction to a message/item/customer balance (spec
/// section 16) - physical count differences, weighing differences, data
/// entry corrections. Always records before/after so the correction itself
/// is fully traceable; once created it is never silently edited (spec:
/// "Finalized adjustments cannot be silently edited").
/// </summary>
public class StockAdjustment : AuditableEntity
{
    public string AdjustmentNumber { get; private set; } = string.Empty; // e.g. "ADJ-2026-000031"
    public DateTime AdjustmentDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid RawMessageId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public AdjustmentType Type { get; private set; }

    public decimal? QuantityBeforeKg { get; private set; }
    public decimal? QuantityBeforeMeter { get; private set; }
    public decimal? AdjustmentQuantityKg { get; private set; }
    public decimal? AdjustmentQuantityMeter { get; private set; }
    public decimal? QuantityAfterKg { get; private set; }
    public decimal? QuantityAfterMeter { get; private set; }

    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? ApprovedBy { get; private set; }

    private StockAdjustment() { } // EF Core

    public StockAdjustment(
        string adjustmentNumber, DateTime adjustmentDate, Guid customerId, Guid itemId, Guid rawMessageId, Guid warehouseId,
        AdjustmentType type, decimal? quantityBeforeKg, decimal? quantityBeforeMeter,
        decimal? adjustmentQuantityKg, decimal? adjustmentQuantityMeter, string reason, string createdBy,
        string? notes = null, string? approvedBy = null)
    {
        if (adjustmentQuantityKg is null && adjustmentQuantityMeter is null)
            throw new ArgumentException("An adjustment must specify a KG and/or Meter quantity.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for every stock adjustment.", nameof(reason));

        var sign = type == AdjustmentType.Increase ? 1 : -1;

        AdjustmentNumber = adjustmentNumber;
        AdjustmentDate = adjustmentDate;
        CustomerId = customerId;
        ItemId = itemId;
        RawMessageId = rawMessageId;
        WarehouseId = warehouseId;
        Type = type;
        QuantityBeforeKg = quantityBeforeKg;
        QuantityBeforeMeter = quantityBeforeMeter;
        AdjustmentQuantityKg = adjustmentQuantityKg;
        AdjustmentQuantityMeter = adjustmentQuantityMeter;
        QuantityAfterKg = quantityBeforeKg.HasValue ? quantityBeforeKg + sign * (adjustmentQuantityKg ?? 0) : null;
        QuantityAfterMeter = quantityBeforeMeter.HasValue ? quantityBeforeMeter + sign * (adjustmentQuantityMeter ?? 0) : null;
        Reason = reason;
        Notes = notes;
        ApprovedBy = approvedBy;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;

        Lock(); // finalized the moment it's created - corrections need a NEW adjustment, never an edit
    }
}
