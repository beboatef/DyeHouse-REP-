using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Append-only ledger for material/chemical stock (spec section 18 applied
/// to materials) - kept as its own table rather than reusing
/// InventoryTransaction, since materials are factory-owned (no CustomerId
/// or RawMessageId dimension), unlike fabric raw stock.
/// </summary>
public class MaterialTransaction : BaseEntity
{
    public DocumentType SourceDocumentType { get; private set; }
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid? ProductionOrderId { get; private set; }
    public decimal Quantity { get; private set; }
    public MaterialTransactionDirection Direction { get; private set; }
    public decimal? UnitCost { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private MaterialTransaction() { } // EF Core

    public MaterialTransaction(
        DocumentType sourceDocumentType, string sourceDocumentNumber, Guid sourceDocumentId,
        DateTime transactionDate, Guid materialId, Guid warehouseId, Guid? productionOrderId,
        decimal quantity, MaterialTransactionDirection direction, decimal? unitCost, string createdBy)
    {
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        SourceDocumentId = sourceDocumentId;
        TransactionDate = transactionDate;
        MaterialId = materialId;
        WarehouseId = warehouseId;
        ProductionOrderId = productionOrderId;
        Quantity = quantity;
        Direction = direction;
        UnitCost = unitCost;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
