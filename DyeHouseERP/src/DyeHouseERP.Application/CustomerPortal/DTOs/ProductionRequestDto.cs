using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.CustomerPortal.DTOs;

public class ProductionRequestDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? RequestedQuantityKg { get; set; }
    public decimal? RequestedQuantityMeter { get; set; }
    public string? Notes { get; set; }
    public ProductionRequestStatus Status { get; set; }
    public Guid? ConvertedProductionOrderId { get; set; }
    public string? ConvertedOrderNumber { get; set; }
    public string? StaffNotes { get; set; }
}
