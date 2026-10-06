namespace DyeHouseERP.Application.CustomerReturns.DTOs;

public class CustomerReturnLineDto
{
    public Guid Id { get; set; }

    /// <summary>The raw lot the returned quantity was added back to - always present.</summary>
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;

    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    public decimal? QuantityKg { get; set; }
    public decimal? QuantityMeter { get; set; }

    /// <summary>Present when the originating Job Order was known (spec section 32).</summary>
    public Guid? ProductionOrderId { get; set; }
    public string? ProductionOrderNumber { get; set; }

    /// <summary>Present when the originating formation basin was known.</summary>
    public Guid? FormationGroupId { get; set; }
    public int? FormationGroupNumber { get; set; }

    public string? Notes { get; set; }
}

public class CustomerReturnDto
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }

    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Optional free text - a return without a stated reason is valid.</summary>
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public List<CustomerReturnLineDto> Lines { get; set; } = new();
}
