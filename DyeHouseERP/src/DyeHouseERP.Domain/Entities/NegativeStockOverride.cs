using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Records an authorized exception to the normal "never allow negative
/// stock" rule (spec section 17). Created only when a transaction that
/// would take a balance negative is explicitly approved by a user holding
/// the inventory.allow_negative_stock permission. Feeds the
/// "Negative Stock / Balance Override Report".
/// </summary>
public class NegativeStockOverride : BaseEntity
{
    public Guid RawMessageId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal BalanceBefore { get; private set; }
    public decimal ResultingBalance { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public string ApprovedBy { get; private set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; private set; }

    private NegativeStockOverride() { } // EF Core

    public NegativeStockOverride(
        Guid rawMessageId, Guid itemId, Guid customerId,
        decimal requestedQuantity, decimal balanceBefore,
        string reason, string requestedBy, string approvedBy)
    {
        RawMessageId = rawMessageId;
        ItemId = itemId;
        CustomerId = customerId;
        RequestedQuantity = requestedQuantity;
        BalanceBefore = balanceBefore;
        ResultingBalance = balanceBefore - requestedQuantity;
        Reason = reason;
        RequestedBy = requestedBy;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
    }
}
