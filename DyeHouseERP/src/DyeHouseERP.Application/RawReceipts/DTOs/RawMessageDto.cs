using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.RawReceipts.DTOs;

public class RawMessageLineDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? Notes { get; set; }

    /// <summary>Rejected at receiving inspection, if any (spec sections 8-9). Inspection is recorded information, never an approval gate.</summary>
    public decimal? RejectedQuantityKg { get; set; }
    public decimal? RejectedQuantityMeter { get; set; }

    /// <summary>Received minus rejected - what remains the customer's allocatable stock.</summary>
    public decimal? AcceptedQuantityKg { get; set; }
    public decimal? AcceptedQuantityMeter { get; set; }

    /// <summary>Remaining un-allocated balance for this line - computed from the inventory ledger, never stored.</summary>
    public decimal? RemainingKg { get; set; }
    public decimal? RemainingMeter { get; set; }
}

public class RawMessageDto
{
    public Guid Id { get; set; }
    public string MessageNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ReceivingUser { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public InspectionStatus InspectionStatus { get; set; }
    public RawMessageStatus Status { get; set; }
    public bool HasRejections { get; set; }
    public List<RawMessageLineDto> Lines { get; set; } = new();
}

public class RawMessageLineInput
{
    public Guid ItemId { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// One line's rejected quantity being recorded at receiving inspection
/// (spec sections 8-9). The rejected part stays traceable but stops being
/// allocatable; nothing is ever balance-edited by hand.
/// </summary>
public class RawMessageLineRejectionInput
{
    public Guid LineId { get; set; }
    public decimal? RejectedQuantityKg { get; set; }
    public decimal? RejectedQuantityMeter { get; set; }
}
