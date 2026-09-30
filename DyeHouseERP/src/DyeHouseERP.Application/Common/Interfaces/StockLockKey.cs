namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// The canonical stock dimension a lock is taken on (H6). Every component is
/// optional so one key type covers all four ledgers the system maintains:
/// raw/customer inventory (ItemId + CustomerId + RawMessageId), ready goods
/// (ItemId + CustomerId + ProductionOrderId), materials/supplies
/// (MaterialId + WarehouseId) and warehouse-only stock.
///
/// The resource string is what the database lock is actually keyed on, so two
/// different dimensions can never collide, and two handlers that touch the
/// SAME dimension always produce byte-identical keys and therefore queue up
/// behind each other.
/// </summary>
public readonly record struct StockLockKey
{
    public Guid? WarehouseId { get; init; }
    public Guid? ItemId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? ProductionOrderId { get; init; }
    public Guid? RawMessageId { get; init; }
    public Guid? MaterialId { get; init; }

    /// <summary>Raw inventory lot dimension: (warehouse, item, customer, message).</summary>
    public static StockLockKey RawLot(Guid warehouseId, Guid itemId, Guid customerId, Guid rawMessageId)
        => new() { WarehouseId = warehouseId, ItemId = itemId, CustomerId = customerId, RawMessageId = rawMessageId };

    /// <summary>
    /// R2: ready-goods dimension that matches the EXACT filter used when ready
    /// balance is read - (item, production order) ONLY, with no warehouse and no
    /// customer term.
    ///
    /// This matters: the ready-balance query filters on RawMessageId == null,
    /// ProductionOrderId and ItemId only, so a lock keyed on a warehouse or a
    /// customer-supplied value would NOT serialize two operations that read the
    /// same balance (e.g. a delivery that locked the ready warehouse it looked
    /// up versus a transfer that locked the request's warehouse). With this key,
    /// every request that reads or writes that order's ready lot - whichever
    /// warehouse either path resolved - queues behind the same lock.
    /// </summary>
    public static StockLockKey ReadyLot(Guid itemId, Guid productionOrderId)
        => new() { ItemId = itemId, ProductionOrderId = productionOrderId };

    /// <summary>Material/supply dimension: (warehouse, material).</summary>
    public static StockLockKey MaterialLot(Guid warehouseId, Guid materialId)
        => new() { WarehouseId = warehouseId, MaterialId = materialId };

    /// <summary>
    /// Stable, order-insensitive textual form of the key. Every id is lower-case
    /// 'D' format (or "any"), so the same logical dimension always maps to the
    /// same lock resource regardless of who built the key.
    /// </summary>
    public string Resource =>
        $"Stock:{Format(WarehouseId)}:{Format(ItemId)}:{Format(CustomerId)}:" +
        $"{Format(ProductionOrderId)}:{Format(RawMessageId)}:{Format(MaterialId)}";

    private static string Format(Guid? id) => id.HasValue ? id.Value.ToString("D") : "any";
}
