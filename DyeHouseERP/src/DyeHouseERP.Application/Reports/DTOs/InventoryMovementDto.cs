using DyeHouseERP.Domain.Entities;

namespace DyeHouseERP.Application.Reports.DTOs;

/// <summary>
/// One row of the inventory ledger exactly as it was posted (spec section 10):
/// source document, date, user, warehouse, item, quantity, unit, customer,
/// raw material message, job order and batch/reference.
/// </summary>
public class InventoryMovementDto
{
    public Guid Id { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public string SourceDocumentNumber { get; set; } = string.Empty;
    public Guid SourceDocumentId { get; set; }
    public DateTime TransactionDate { get; set; }

    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    public Guid? RawMessageId { get; set; }
    public string? MessageNumber { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public string? OrderNumber { get; set; }

    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public TransactionDirection Direction { get; set; }

    /// <summary>Quantity with the ledger direction applied (in = positive, out = negative) - the running-balance building block.</summary>
    public decimal? SignedQuantityKg { get; set; }
    public decimal? SignedQuantityMeter { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public Guid? ReversesTransactionId { get; set; }
}
