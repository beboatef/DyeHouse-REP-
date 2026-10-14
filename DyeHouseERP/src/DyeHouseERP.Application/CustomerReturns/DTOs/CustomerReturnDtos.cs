using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.CustomerReturns.DTOs;

public class CustomerReturnLineDto
{
    public Guid Id { get; set; }

    /// <summary>The raw lot the returned quantity was added back to - always present.</summary>
    public Guid RawMessageId { get; set; }
    public string MessageNumber { get; set; } = string.Empty;

    /// <summary>Where the returned material actually landed (spec section 32's destination): the lot's own warehouse, always a raw-material one.</summary>
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;

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

    /// <summary>The document the goods came back against (spec section 32), when one was stated.</summary>
    public string? ReferenceDocument { get; set; }

    /// <summary>Whose material came back - the customer's or the factory's own (spec section 32).</summary>
    public ReturnOwnership Ownership { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>True once the return was cancelled through the correction workflow (spec section 49).
    /// The document and its original ledger rows stay; the stock it restored is reversed.</summary>
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public List<CustomerReturnLineDto> Lines { get; set; } = new();

    /// <summary>
    /// The Job Orders raised FROM this return, if any (spec section 33). Read-only traceability: the
    /// return is never converted, so this is a reference list and not a state of the return.
    /// </summary>
    public List<CustomerReturnOrderDto> ProductionOrders { get; set; } = new();
}

/// <summary>One Job Order raised from a return - its number and who carries its cost.</summary>
public class CustomerReturnOrderDto
{
    public Guid ProductionOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public CostBearer CostBearer { get; set; }
    public ProductionOrderStatus Status { get; set; }
}
