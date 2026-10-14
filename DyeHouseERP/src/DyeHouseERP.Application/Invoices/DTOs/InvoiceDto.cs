using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Invoices.DTOs;

public class InvoiceLineDto
{
    public Guid Id { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public string? ProductionOrderNumber { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string? Color { get; set; }
    /// <summary>The unit the service is BILLED in, taken from the order's configured service price.</summary>
    public UnitOfMeasure Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal ProcessingPrice { get; set; }
    public decimal Value { get; set; }

    /// <summary>Set when this line bills an explicitly chargeable cost entry, so the link is visible.</summary>
    public Guid? CostEntryId { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    /// <summary>The delivery this invoice was raised from, if any. Editable while the invoice is a draft.</summary>
    public Guid? DeliveryId { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
}

public class InvoiceLineInput
{
    public Guid? ProductionOrderId { get; set; }
    public Guid ItemId { get; set; }
    public string? Color { get; set; }

    /// <summary>
    /// The unit this line is billed in. Defaults to KG for a caller that omits it, but the value the
    /// guard checks is what the caller SENT: billing a job in Meter requires a Meter service price
    /// and an approved Meter quantity, never a converted KG figure.
    /// </summary>
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.KG;

    public decimal Quantity { get; set; }
    public decimal ProcessingPrice { get; set; }
    public string? Notes { get; set; }

    /// <summary>A cost entry explicitly flagged chargeable to the customer, billed by this line.</summary>
    public Guid? CostEntryId { get; set; }
}

public class CustomerStatementLineDto
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}
