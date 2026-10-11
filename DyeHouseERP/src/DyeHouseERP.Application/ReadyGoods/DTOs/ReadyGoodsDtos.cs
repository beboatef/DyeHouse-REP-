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
    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }

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
}
