using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.StockAdjustments.DTOs;

public class StockAdjustmentDto
{
    public Guid Id { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public AdjustmentType Type { get; set; }
    public decimal? QuantityBeforeKg { get; set; }
    public decimal? QuantityBeforeMeter { get; set; }
    public decimal? AdjustmentQuantityKg { get; set; }
    public decimal? AdjustmentQuantityMeter { get; set; }
    public decimal? QuantityAfterKg { get; set; }
    public decimal? QuantityAfterMeter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ApprovedBy { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
