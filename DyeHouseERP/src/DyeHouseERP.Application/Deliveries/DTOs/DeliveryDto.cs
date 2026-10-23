using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Entities;

namespace DyeHouseERP.Application.Deliveries.DTOs;

public class DeliveryLineDto
{
    public Guid Id { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// The ORIGINAL Job Order date (spec section 31). Read live from the order - never copied onto the
    /// delivery - so a later partial-delivery document shows the date the job was actually taken in,
    /// not the date of the delivery it happens to appear on.
    /// </summary>
    public DateTime? ProductionOrderDate { get; set; }

    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public ReadyGoodsCategory? Category { get; set; }
    public string? RawOrigin { get; set; }
    public List<string> DrawingNumbers { get; set; } = new();

    // ---- Original raw accounting (spec section 31) ----

    /// <summary>Original raw quantity this line settles - manually entered per delivery. KG and Meter stay independent.</summary>
    public decimal? OriginalRawQuantityKg { get; set; }
    public decimal? OriginalRawQuantityMeter { get; set; }

    /// <summary>The order's ORIGINAL raw quantity: fixed at the issue and never overwritten by a delivery.</summary>
    public decimal? OriginalOrderRawQuantityKg { get; set; }
    public decimal? OriginalOrderRawQuantityMeter { get; set; }

    /// <summary>Original PO quantity minus what every non-cancelled delivery of this order has settled so far.</summary>
    public decimal RemainingOriginalRawKg { get; set; }
    public decimal RemainingOriginalRawMeter { get; set; }
}

public class DeliveryDto
{
    public Guid Id { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; }
    public string? Notes { get; set; }

    // ---- Primary / sub-customer permit (spec section 31) ----

    /// <summary>True when this document is a sub-customer permit issued against a primary one.</summary>
    public bool IsSubCustomerPermit { get; set; }
    public Guid? PrimaryDeliveryId { get; set; }

    /// <summary>The primary permit's number - the one cross-reference a sub-customer permit carries.</summary>
    public string? PrimaryDeliveryNumber { get; set; }

    // The primary CUSTOMER is identified only by code and name on the sub-customer's permit. Nothing
    // else of theirs travels with it: no ledger, no charges, no contact details.
    public Guid? PrimaryCustomerId { get; set; }
    public string? PrimaryCustomerCode { get; set; }
    public string? PrimaryCustomerName { get; set; }

    public List<DeliveryLineDto> Lines { get; set; } = new();
}

public class DeliveryLineInput
{
    public Guid ProductionOrderId { get; set; }
    public Guid ItemId { get; set; }
    public string? Color { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }
    public int? PieceCount { get; set; }
    public ReadyGoodsCategory? Category { get; set; }
    public string? RawOrigin { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The original raw quantity this partial delivery settles (spec section 31). Manually entered per
    /// delivery - never derived from the finished quantity. Optional on a line that settles none.
    /// </summary>
    public decimal? OriginalRawQuantityKg { get; set; }
    public decimal? OriginalRawQuantityMeter { get; set; }
}

/// <summary>
/// One row of the printable DELIVERY STATEMENT (بيان تسليم): the production accounting behind a
/// delivered line - what the job started from, what came out, and what the difference is.
///
/// All quantities are reported per unit and are NEVER converted into one another: a figure is null
/// when that unit was not recorded, and calculated waste is computed within a unit only.
/// </summary>
public class DeliveryStatementRowDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;

    /// <summary>The ORIGINAL Job Order date, preserved on every later partial-delivery statement.</summary>
    public DateTime? ProductionOrderDate { get; set; }

    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public ReadyGoodsCategory? Category { get; set; }

    /// <summary>The order's original raw quantity (fixed, never overwritten).</summary>
    public decimal? OriginalRawQuantityKg { get; set; }
    public decimal? OriginalRawQuantityMeter { get; set; }

    /// <summary>Original raw this delivery settles (manually entered).</summary>
    public decimal? DeliveredOriginalRawQuantityKg { get; set; }
    public decimal? DeliveredOriginalRawQuantityMeter { get; set; }

    /// <summary>The order's original raw quantity minus everything its deliveries have settled.</summary>
    public decimal RemainingOriginalRawKg { get; set; }
    public decimal RemainingOriginalRawMeter { get; set; }

    /// <summary>What this delivery actually hands over - the two units are independent.</summary>
    public decimal? ReadyGoodsQuantityKg { get; set; }
    public decimal? ReadyGoodsQuantityMeter { get; set; }

    /// <summary>Bale/roll count - auxiliary: descriptive, never used in any calculation.</summary>
    public int? BaleCount { get; set; }

    /// <summary>Scraps / separates recorded by the order's operations, per unit.</summary>
    public decimal? ScrapsKg { get; set; }
    public decimal? ScrapsMeter { get; set; }

    /// <summary>Second-grade material recorded by the order's operations, per unit.</summary>
    public decimal? SecondGradeKg { get; set; }
    public decimal? SecondGradeMeter { get; set; }
    public int? SecondGradePieceCount { get; set; }

    /// <summary>
    /// Calculated waste = original raw - ready goods - scraps/separates - second grade, computed
    /// SEPARATELY for KG and for Meter and never mixing them (see the query for the exact rule).
    /// Null when the unit has no original raw figure to measure against.
    /// </summary>
    /// <summary>
    /// The waste of the line's job order - original raw minus ready goods minus scraps minus second
    /// grade. A WEIGHT, so it exists in KILOGRAMS ONLY: the metre figures stay a length and are never
    /// converted into a weight.
    /// </summary>
    public decimal? CalculatedWasteKg { get; set; }

    /// <summary>The drawing number(s) of the order's printing operations, when it passed Printing.</summary>
    public List<string> DrawingNumbers { get; set; } = new();

    public string? LineNotes { get; set; }
}

/// <summary>The delivery statement header + its rows (spec section 31).</summary>
public class DeliveryStatementDto
{
    public Guid DeliveryId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public DeliveryStatus Status { get; set; }
    public string? Notes { get; set; }

    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Set when this statement belongs to a sub-customer permit: the primary permit it was issued against.</summary>
    public string? PrimaryDeliveryNumber { get; set; }
    public string? PrimaryCustomerCode { get; set; }
    public string? PrimaryCustomerName { get; set; }

    public List<DeliveryStatementRowDto> Rows { get; set; } = new();
}
