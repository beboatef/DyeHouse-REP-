using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.MaterialSales.DTOs;

public class MaterialSaleLineDto
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal AvailableBalance { get; set; }
    public string? Description { get; set; }
}

public class MaterialSaleDto
{
    public Guid Id { get; set; }
    public string SaleNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string BuyerName { get; set; } = string.Empty;

    public Guid? TreasuryAccountId { get; set; }
    public string? TreasuryAccountName { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }

    public MaterialSaleStatus Status { get; set; }
    public bool IsEditable { get; set; }

    public string? PostedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public List<MaterialSaleLineDto> Lines { get; set; } = new();
}
