using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>Transfer of a material between two material warehouses (spec section 26).</summary>
public class MaterialTransfer : AuditableEntity
{
    public string TransferNumber { get; private set; } = string.Empty;
    public DateTime TransferDate { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public decimal Quantity { get; private set; }
    public string? Notes { get; private set; }

    private MaterialTransfer() { } // EF Core

    public MaterialTransfer(string transferNumber, DateTime transferDate, Guid materialId,
        Guid fromWarehouseId, Guid toWarehouseId, decimal quantity, string createdBy, string? notes = null)
    {
        if (fromWarehouseId == toWarehouseId) throw new ArgumentException("Source and destination warehouse must differ.");
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));

        TransferNumber = transferNumber;
        TransferDate = transferDate;
        MaterialId = materialId;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        Quantity = quantity;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
