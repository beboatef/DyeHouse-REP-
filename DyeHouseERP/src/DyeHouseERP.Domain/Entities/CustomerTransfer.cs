using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Transfers ownership of part of a raw message's quantity from one
/// customer to another (spec section 15). The RawMessage's own CustomerId
/// is NEVER edited - this transaction is the sole, permanent record of the
/// ownership change, and the inventory ledger's per-customer balance (see
/// IInventoryLedgerService.GetCustomerBalanceAsync) is what actually shifts.
/// </summary>
public class CustomerTransfer : AuditableEntity
{
    public string TransferNumber { get; private set; } = string.Empty; // system-generated, e.g. "TRF-2026-000012"
    public DateTime TransferDate { get; private set; }
    public Guid FromCustomerId { get; private set; }
    public Guid ToCustomerId { get; private set; }
    public Guid RawMessageId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public bool ApprovalRequired { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    private CustomerTransfer() { } // EF Core

    public CustomerTransfer(
        string transferNumber, DateTime transferDate, Guid fromCustomerId, Guid toCustomerId,
        Guid rawMessageId, Guid itemId, decimal? quantityKg, decimal? quantityMeter,
        string reason, string createdBy, string? notes = null, bool approvalRequired = false, string? approvedBy = null)
    {
        if (fromCustomerId == toCustomerId)
            throw new ArgumentException("Cannot transfer raw material from a customer to themselves.");
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A customer transfer must specify a KG and/or Meter quantity.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a customer-to-customer transfer.", nameof(reason));
        if (approvalRequired && string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("This transfer requires approval - provide an approver.", nameof(approvedBy));

        TransferNumber = transferNumber;
        TransferDate = transferDate;
        FromCustomerId = fromCustomerId;
        ToCustomerId = toCustomerId;
        RawMessageId = rawMessageId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        Reason = reason;
        Notes = notes;
        ApprovalRequired = approvalRequired;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = approvalRequired ? DateTime.UtcNow : null;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
