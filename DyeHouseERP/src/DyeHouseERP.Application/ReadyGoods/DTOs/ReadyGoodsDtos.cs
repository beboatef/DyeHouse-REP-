namespace DyeHouseERP.Application.ReadyGoods.DTOs;

public class ReadyGoodsTransferDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;

    /// <summary>The item's name as well as its code - the receipt record has to read as a record, not an id list.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>The customer's name as well as its code (spec section 29's receipt fields).</summary>
    public string CustomerName { get; set; } = string.Empty;

    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? Category { get; set; }

    /// <summary>Where the output was physically received - the Ready Goods warehouse (spec section 29).</summary>
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    /// <summary>
    /// What is still available on this transfer's lot right now, live from the ledger (spec section 29's
    /// "available quantity"): the transferred quantity minus everything delivered or moved back to raw
    /// since. Never a stored figure that could drift.
    /// </summary>
    public decimal AvailableQuantityKg { get; set; }
    public decimal AvailableQuantityMeter { get; set; }

    /// <summary>The stage execution whose output this transfer moved (spec section 23). Null for pre-existing rows only.</summary>
    public Guid? StageExecutionId { get; set; }
    public int? StageSequence { get; set; }
    public string? StageName { get; set; }
    public decimal? StageOutputKg { get; set; }
    public decimal? StageOutputMeter { get; set; }

    /// <summary>Posted or Cancelled - a cancelled transfer's stock effect is zeroed by its reversal rows.</summary>
    public string Status { get; set; } = "Posted";
}

public class ReadyGoodsBalanceDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal RemainingKg { get; set; }
    public decimal RemainingMeter { get; set; }
    public string? Category { get; set; }
}
