using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Transfers completed production output into the Ready Goods warehouse
/// (spec section 29 - "ترحيل"). Validated against the order's stage
/// completion before posting; the ledger row this creates (WarehouseId =
/// ready warehouse, RawMessageId = null, ProductionOrderId set) is what
/// both "production output" and "ready warehouse receipt" resolve to.
/// </summary>
public class ReadyGoodsTransfer : AuditableEntity
{
    public string TransferNumber { get; private set; } = string.Empty;
    public DateTime TransferDate { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public int? PieceCount { get; private set; }
    public string? Notes { get; private set; }

    private ReadyGoodsTransfer() { } // EF Core

    public ReadyGoodsTransfer(
        string transferNumber, DateTime transferDate, Guid productionOrderId, Guid customerId, Guid itemId,
        Guid warehouseId, decimal? quantityKg, decimal? quantityMeter, string createdBy,
        int? pieceCount = null, string? notes = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A ready goods transfer must specify a KG and/or Meter quantity.");

        TransferNumber = transferNumber;
        TransferDate = transferDate;
        ProductionOrderId = productionOrderId;
        CustomerId = customerId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
    public void UpdateDetails(
        DateTime transferDate,
        decimal? quantityKg,
        decimal? quantityMeter,
        int? pieceCount,
        string? notes)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("KG and/or Meter quantity is required.");

        TransferDate = transferDate;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        Notes = notes;
    }

}
