using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.RawReceipts.DTOs;

public class RawMessageLineDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? Notes { get; set; }

    /// <summary>Remaining un-allocated balance for this line - computed from the inventory ledger, never stored.</summary>
    public decimal? RemainingKg { get; set; }
    public decimal? RemainingMeter { get; set; }
}

public class RawMessageDto
{
    public Guid Id { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ReceivingUser { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public InspectionStatus InspectionStatus { get; set; }
    public RawMessageStatus Status { get; set; }
    public List<RawMessageLineDto> Lines { get; set; } = new();
}

public class RawMessageLineInput
{
    public Guid ItemId { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? Notes { get; set; }
}
