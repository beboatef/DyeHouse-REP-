namespace DyeHouseERP.Application.ReadyGoods.DTOs;

/// <summary>
/// One authorized movement of ready goods back into raw-material stock (spec section 29). Read-only
/// projection of a posted document: the ledger rows it created are never edited, so the register is
/// simply a view over them.
/// </summary>
public class ReadyGoodsRawReturnDto
{
    public Guid Id { get; set; }
    public string MovementNumber { get; set; } = string.Empty;
    public DateTime MovementDate { get; set; }

    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;

    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;

    /// <summary>The Ready Goods warehouse the stock left.</summary>
    public Guid SourceWarehouseId { get; set; }
    public string SourceWarehouseName { get; set; } = string.Empty;

    /// <summary>The raw material lot it went back onto, and that lot's warehouse.</summary>
    public Guid DestinationRawMessageId { get; set; }
    public string DestinationMessageNumber { get; set; } = string.Empty;
    public Guid DestinationWarehouseId { get; set; }
    public string DestinationWarehouseName { get; set; } = string.Empty;

    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public string? Category { get; set; }

    /// <summary>Mandatory on the document - a reclassification of posted stock is never silent.</summary>
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
