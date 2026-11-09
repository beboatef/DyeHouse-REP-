namespace DyeHouseERP.Application.Reports.DTOs;

/// <summary>
/// One Job Order's waste, DERIVED from its four streams. Nothing on this row is a posted balance -
/// the approved policy is that waste is a production loss and never becomes stock.
/// </summary>
public class WasteReportRowDto
{
    public Guid ProductionOrderId { get; set; }
    public string ProductionOrderNumber { get; set; } = string.Empty;
    public DateTime? ProductionOrderDate { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    /// <summary>أصل الخام - the original raw weight the Job Order started from.</summary>
    public decimal? OriginalRawKg { get; set; }

    /// <summary>The first-grade ready weight actually posted for this order.</summary>
    public decimal? FirstGradeReadyKg { get; set; }

    public decimal? SecondGradeKg { get; set; }
    public decimal? SeparatesKg { get; set; }

    /// <summary>الهالك - null when it cannot be derived, never a fabricated zero.</summary>
    public decimal? WasteKg { get; set; }

    public decimal? WastePercentKg { get; set; }

    /// <summary>Why the waste is null, when it is. Null when the figure was derived successfully.</summary>
    public string? Note { get; set; }
}

public class WasteReportDto
{
    public List<WasteReportRowDto> Rows { get; set; } = new();

    public int OrderCount => Rows.Count;

    public decimal? TotalOriginalRawKg { get; set; }
    public decimal? TotalFirstGradeReadyKg { get; set; }
    public decimal? TotalSecondGradeKg { get; set; }
    public decimal? TotalSeparatesKg { get; set; }
    public decimal? TotalWasteKg { get; set; }
    public decimal? TotalWastePercentKg { get; set; }
}
