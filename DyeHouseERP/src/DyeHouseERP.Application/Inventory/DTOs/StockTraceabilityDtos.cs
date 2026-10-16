namespace DyeHouseERP.Application.Inventory.DTOs;

/// <summary>
/// One BALANCE ROW of a stock traceability screen: one lot, not one item.
///
/// The unit of traceability differs per warehouse - a raw-material lot IS a receipt message, a
/// ready-goods lot IS a job order, and a material lot is a (material, warehouse) pair - so the row
/// carries the identity of whichever of them applies and leaves the others null. Nothing is
/// invented: a figure that was never recorded stays null, and KG and Meter are reported in their
/// own fields and are never added together or converted into one another.
/// </summary>
public class StockTraceRowDto
{
    /// <summary>raw | ready | materials - which warehouse this row belongs to.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// The identity of THIS balance row, and the one key that ties it to the movements it is made of: a
    /// raw lot is (receipt message, item, customer), a ready lot is (job order, item), and a material
    /// lot is (material, warehouse). No two rows share it, and every movement carries its own row's
    /// value - so a screen, a report or a printed card can match a movement to its balance WITHOUT
    /// re-deriving the rule, which is what keeps one item's stock from being reported under another
    /// item's name. Format is the query's own and is not part of the contract: compare it, do not parse
    /// it (the identity FIELDS below are the readable ones).
    /// </summary>
    public string LotKey { get; set; } = string.Empty;

    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    /// <summary>The item's own configured units, so the screen never shows a unit the item does not have.</summary>
    public string BaseUnit { get; set; } = string.Empty;
    public string? SecondaryUnit { get; set; }

    // ---- raw material identity (one receipt message = one lot) ----
    public Guid? RawMessageId { get; set; }
    public string? MessageNumber { get; set; }
    public DateTime? MessageDate { get; set; }
    public string? MessageStatus { get; set; }

    // ---- job order identity (raw consumption and the ready-goods lot) ----
    public Guid? ProductionOrderId { get; set; }
    public string? OrderNumber { get; set; }
    /// <summary>When a raw receipt or material lot was issued to several job orders, all of their numbers.</summary>
    public string? OrderNumbers { get; set; }
    public DateTime? OrderDate { get; set; }
    public string? OrderStatus { get; set; }
    public string? Color { get; set; }
    /// <summary>Ready goods only: piece count recorded on the ready-goods transfer when available.</summary>
    public int? ProducedPieceCount { get; set; }

    // ---- material / chemical identity ----
    public Guid? MaterialId { get; set; }
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    /// <summary>The material's OWN unit (KG / Gram / Liter) - never forced to KG/Meter.</summary>
    public string? MaterialUnit { get; set; }
    public string? MaterialKind { get; set; }

    // ---- warehouse, customer and supplier ----
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }

    /// <summary>Materials/chemicals only: where the stock was bought (spec section 35).</summary>
    public string? SupplierName { get; set; }
    /// <summary>Materials/chemicals only: the purchase order number, when the receipt was against one.</summary>
    public string? PurchaseOrderNumber { get; set; }

    // ---- quantities, each in its own unit ----
    /// <summary>Received into stock: a raw receipt, or a purchase receipt for a material.</summary>
    public decimal? ReceivedKg { get; set; }
    public decimal? ReceivedMeter { get; set; }
    public decimal? ReceivedQuantity { get; set; }

    /// <summary>Issued to production (a raw issue to a job order, or a material issue to one).</summary>
    public decimal? IssuedToProductionKg { get; set; }
    public decimal? IssuedToProductionMeter { get; set; }
    public decimal? IssuedToProductionQuantity { get; set; }

    /// <summary>Raw material issued to job orders that are still open - material genuinely under production.</summary>
    public decimal? UnderProductionKg { get; set; }
    public decimal? UnderProductionMeter { get; set; }

    /// <summary>Returned to the warehouse (customer return, ready-to-raw return, pause release, external return).</summary>
    public decimal? ReturnedKg { get; set; }
    public decimal? ReturnedMeter { get; set; }
    public decimal? ReturnedQuantity { get; set; }

    /// <summary>Produced by this job order (ready goods) - read from the same ledger the ready balance is.</summary>
    public decimal? ProducedKg { get; set; }
    public decimal? ProducedMeter { get; set; }

    /// <summary>Net delivered to the customer (deliveries minus their reversals).</summary>
    public decimal? DeliveredKg { get; set; }
    public decimal? DeliveredMeter { get; set; }
    public int? DeliveredPieceCount { get; set; }
    public string? DeliveryNumbers { get; set; }
    public DateTime? LastDeliveryDate { get; set; }

    /// <summary>The lot's CURRENT balance, summed live from the ledger - never a stored field.</summary>
    public decimal? RemainingKg { get; set; }
    public decimal? RemainingMeter { get; set; }
    public decimal? RemainingQuantity { get; set; }

    public int MovementCount { get; set; }
}

/// <summary>
/// One ledger row behind a balance, with the balance AS IT STOOD after that row. Balances are never
/// stored: this is computed by running the ledger forward inside one lot, which is also what proves
/// the total on screen is the sum of exactly these movements and nothing else.
/// </summary>
public class StockTraceMovementDto
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// The balance row this movement belongs to - the same value as the matching
    /// <see cref="StockTraceRowDto.LotKey"/>, and the running balance printed on this row is this lot's
    /// balance, never a total of several lots. Two items inside one receipt message are two lots with
    /// two different keys, so their movements are never mixed.
    /// </summary>
    public string LotKey { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }

    /// <summary>Received | Issued | IssuedExternal | Returned | TransferredIn/Out | Adjusted | Produced | Delivered | In/Out.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public bool IsReversal { get; set; }

    /// <summary>The lot this row belongs to, so the row can be matched to its balance row.</summary>
    public Guid? RawMessageId { get; set; }
    public string? MessageNumber { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? MaterialId { get; set; }
    public string? MaterialCode { get; set; }
    public Guid? ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? DeliveryId { get; set; }

    /// <summary>Materials/chemicals only: supplier and purchase order of a receipt (spec section 35).</summary>
    public string? SupplierName { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    // Fabric movements carry KG and Meter; material movements carry one quantity in the material's unit.
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }

    /// <summary>Running balance within the lot after this row, per unit.</summary>
    public decimal? BalanceAfterKg { get; set; }
    public decimal? BalanceAfterMeter { get; set; }
    public decimal? BalanceAfter { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The whole screen in one response: the balance rows, the movements behind them, and the totals.
/// Totals are per unit and are never merged into one number (KG and Meter are independent).
/// </summary>
public class StockTraceabilityDto
{
    public string Scope { get; set; } = string.Empty;

    public List<StockTraceRowDto> Rows { get; set; } = new();
    public List<StockTraceMovementDto> Movements { get; set; } = new();

    public decimal? TotalRemainingKg { get; set; }
    public decimal? TotalRemainingMeter { get; set; }
    public decimal? TotalRemainingQuantity { get; set; }

    public decimal? TotalUnderProductionKg { get; set; }
    public decimal? TotalUnderProductionMeter { get; set; }

    public decimal? TotalReceivedKg { get; set; }
    public decimal? TotalReceivedMeter { get; set; }
    public decimal? TotalReceivedQuantity { get; set; }

    public decimal? TotalIssuedKg { get; set; }
    public decimal? TotalIssuedMeter { get; set; }
    public decimal? TotalIssuedQuantity { get; set; }

    public decimal? TotalReturnedKg { get; set; }
    public decimal? TotalReturnedMeter { get; set; }
    public decimal? TotalReturnedQuantity { get; set; }

    public decimal? TotalProducedKg { get; set; }
    public decimal? TotalProducedMeter { get; set; }

    public decimal? TotalDeliveredKg { get; set; }
    public decimal? TotalDeliveredMeter { get; set; }

    /// <summary>How many ledger rows the filters selected (before any movement-side filtering).</summary>
    public int MovementCount { get; set; }

    /// <summary>When the response was built, so the screen can show how fresh the figures are.</summary>
    public DateTime GeneratedAtUtc { get; set; }
}
