using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

public enum TransactionDirection
{
    In = 1,
    Out = -1
}

/// <summary>
/// Immutable inventory ledger row (spec section 18). Balances are NEVER
/// stored as an editable field anywhere in the system - every screen that
/// shows "current balance" computes it by summing ledger rows filtered by
/// customer/item/message/warehouse. This is what makes negative-stock
/// checking, auditability and full traceability possible.
/// Once written, a row is never updated or deleted - corrections are made
/// by posting an equal-and-opposite reversal row that references it.
/// </summary>
public class InventoryTransaction : BaseEntity
{
    public DocumentType SourceDocumentType { get; private set; }
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }

    public DateTime TransactionDate { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? RawMessageId { get; private set; }
    public Guid? ProductionOrderId { get; private set; }

    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public TransactionDirection Direction { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Set when this row is itself a reversal of an earlier row.</summary>
    public Guid? ReversesTransactionId { get; private set; }

    private InventoryTransaction() { } // EF Core

    public InventoryTransaction(
        DocumentType sourceDocumentType, string sourceDocumentNumber, Guid sourceDocumentId,
        DateTime transactionDate, Guid warehouseId, Guid customerId, Guid itemId,
        Guid? rawMessageId, Guid? productionOrderId,
        decimal? quantityKg, decimal? quantityMeter, TransactionDirection direction,
        string createdBy, Guid? reversesTransactionId = null)
    {
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        SourceDocumentId = sourceDocumentId;
        TransactionDate = transactionDate;
        WarehouseId = warehouseId;
        CustomerId = customerId;
        ItemId = itemId;
        RawMessageId = rawMessageId;
        ProductionOrderId = productionOrderId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        Direction = direction;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
        ReversesTransactionId = reversesTransactionId;
    }
}
