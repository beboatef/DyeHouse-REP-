using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Records that a Production Order consumed a specific quantity from a
/// specific RawMessage (spec section 13). The message is always manually
/// chosen by the user - NEVER auto-selected by FIFO or any other automatic
/// rule (spec section 4). A single order may have several of these, each
/// pointing at a different message, e.g.:
///   PRD-000217:  Message 125 -> 600 KG
///                Message 131 -> 400 KG
/// </summary>
public class RawAllocation : BaseEntity
{
    public Guid ProductionOrderId { get; private set; }
    public Guid RawMessageId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public string AllocatedBy { get; private set; } = string.Empty;
    public DateTime AllocatedAtUtc { get; private set; }

    private RawAllocation() { } // EF Core

    internal RawAllocation(Guid productionOrderId, Guid rawMessageId, Guid itemId,
        decimal? quantityKg, decimal? quantityMeter, string allocatedBy)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A raw allocation must specify a KG and/or Meter quantity.");

        ProductionOrderId = productionOrderId;
        RawMessageId = rawMessageId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        AllocatedBy = allocatedBy;
        AllocatedAtUtc = DateTime.UtcNow;
    }
}
