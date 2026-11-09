namespace DyeHouseERP.Application.Inventory.DTOs;

/// <summary>
/// كارت الصنف لمخزن واحد (BE-3): الرصيد الافتتاحي، حركات الفترة، الرصيد الختامي.
///
/// The card is scoped to ONE warehouse by construction - the ledger rows are read with that warehouse
/// in the WHERE clause, not filtered afterwards on screen - so two warehouses holding the same item can
/// never contribute to one card. Nothing is stored: the opening balance is the signed sum of the rows
/// BEFORE the period, the closing balance is the signed sum of ALL the rows, and the movements in
/// between are exactly the rows of the period, each with the balance as it stood after it. KG and Meter
/// stay in their own fields and are never converted into one another.
/// </summary>
public class ItemCardDto
{
    // ---- which warehouse this card is for (always exactly one) ----
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseKind { get; set; } = string.Empty;
    /// <summary>raw | ready | materials | supplies - the ledger the card was read from.</summary>
    public string Scope { get; set; } = string.Empty;
    /// <summary>True when the card reads the factory-owned material ledger (single unit), false for fabric.</summary>
    public bool IsMaterials { get; set; }

    // ---- the item / material the card is for ----
    public Guid? ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? BaseUnit { get; set; }
    public string? SecondaryUnit { get; set; }

    public Guid? MaterialId { get; set; }
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string? MaterialUnit { get; set; }

    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }

    // ---- the filters the caller used, echoed so the card can state its own scope ----
    public Guid? RawMessageId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // ---- the card itself: opening + in / out + closing, per unit ----
    public decimal? OpeningKg { get; set; }
    public decimal? OpeningMeter { get; set; }
    public decimal? OpeningQuantity { get; set; }

    public decimal? InKg { get; set; }
    public decimal? InMeter { get; set; }
    public decimal? InQuantity { get; set; }

    public decimal? OutKg { get; set; }
    public decimal? OutMeter { get; set; }
    public decimal? OutQuantity { get; set; }

    public decimal? ClosingKg { get; set; }
    public decimal? ClosingMeter { get; set; }
    public decimal? ClosingQuantity { get; set; }

    /// <summary>The rows of the period only, oldest first, each with its running balance. Read-only.</summary>
    public List<StockTraceMovementDto> Movements { get; set; } = new();

    public int MovementCount => Movements.Count;
    public DateTime GeneratedAtUtc { get; set; }
}
