namespace DyeHouseERP.Application.CustomerTransfers.DTOs;

public class CustomerTransferDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public Guid FromCustomerId { get; set; }
    public string FromCustomerCode { get; set; } = string.Empty;
    public string FromCustomerName { get; set; } = string.Empty;
    public Guid ToCustomerId { get; set; }
    public string ToCustomerCode { get; set; } = string.Empty;
    public string ToCustomerName { get; set; } = string.Empty;
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
