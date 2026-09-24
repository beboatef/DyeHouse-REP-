namespace DyeHouseERP.Application.Reports.DTOs;

/// <summary>Feeds the "Negative Stock / Balance Override Report" required by spec section 17.</summary>
public class NegativeStockOverrideDto
{
    public Guid Id { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal ResultingBalance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
}
