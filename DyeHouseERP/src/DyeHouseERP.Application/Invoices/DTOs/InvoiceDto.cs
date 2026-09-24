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
    public decimal Quantity { get; set; }
    public decimal ProcessingPrice { get; set; }
    public decimal Value { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
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
    public decimal Quantity { get; set; }
    public decimal ProcessingPrice { get; set; }
    public string? Notes { get; set; }
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
