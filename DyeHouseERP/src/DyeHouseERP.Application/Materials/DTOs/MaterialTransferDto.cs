namespace DyeHouseERP.Application.Materials.DTOs;

public class MaterialTransferDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public Guid FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public Guid ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class MaterialIssueDto
{
    public Guid Id { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? Notes { get; set; }
}

public class MaterialPreparationDto
{
    public Guid Id { get; set; }
    public string PreparationNumber { get; set; } = string.Empty;
    public DateTime PreparationDate { get; set; }
    public Guid OriginalMaterialId { get; set; }
    public string OriginalMaterialCode { get; set; } = string.Empty;
    public decimal OriginalQuantity { get; set; }
    public decimal WaterQuantity { get; set; }
    public decimal ResultingQuantity { get; set; }
    public decimal? Concentration { get; set; }
    public decimal? Cost { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public string? Notes { get; set; }
}
