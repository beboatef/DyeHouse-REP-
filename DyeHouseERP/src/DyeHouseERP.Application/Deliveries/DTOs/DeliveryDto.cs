using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Deliveries.DTOs;

public class DeliveryLineDto
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? RawOrigin { get; set; }
}

public class DeliveryDto
{
    public Guid Id { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; }
    public string? Notes { get; set; }
    public List<DeliveryLineDto> Lines { get; set; } = new();
}

public class DeliveryLineInput
{
    public Guid ProductionOrderId { get; set; }
    public Guid ItemId { get; set; }
    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? RawOrigin { get; set; }
    public string? Notes { get; set; }
}
