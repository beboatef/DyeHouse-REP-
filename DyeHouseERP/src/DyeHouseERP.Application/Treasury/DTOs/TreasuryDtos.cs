namespace DyeHouseERP.Application.Treasury.DTOs;

public class TreasuryAccountDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
}

public class ReceiptDto
{
    public Guid Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public string TreasuryAccountName { get; set; } = string.Empty;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Description { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public string TreasuryAccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PayeeDescription { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class TreasuryTransferDto
{
    public Guid Id { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public Guid FromAccountId { get; set; }
    public string FromAccountName { get; set; } = string.Empty;
    public Guid ToAccountId { get; set; }
    public string ToAccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
