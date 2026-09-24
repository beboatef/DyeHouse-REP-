using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

public enum TreasuryAccountKind { Cash = 1, Bank = 2 }
public enum TreasuryDirection { In = 1, Out = -1 }

/// <summary>Cash/bank account master (spec section 34).</summary>
public class TreasuryAccount : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public TreasuryAccountKind Kind { get; private set; }
    public bool IsActive { get; private set; } = true;

    private TreasuryAccount() { } // EF Core

    public TreasuryAccount(string code, string name, TreasuryAccountKind kind, string createdBy)
    {
        Code = code.Trim();
        Name = name.Trim();
        Kind = kind;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Append-only cash/bank ledger (spec sections 18 + 34).</summary>
public class TreasuryTransaction : BaseEntity
{
    public Guid TreasuryAccountId { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }
    public decimal Amount { get; private set; }
    public TreasuryDirection Direction { get; private set; }
    public string? Description { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private TreasuryTransaction() { } // EF Core

    public TreasuryTransaction(Guid treasuryAccountId, DateTime transactionDate, DocumentType sourceDocumentType,
        string sourceDocumentNumber, Guid sourceDocumentId, decimal amount, TreasuryDirection direction,
        string? description, string createdBy)
    {
        TreasuryAccountId = treasuryAccountId;
        TransactionDate = transactionDate;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        SourceDocumentId = sourceDocumentId;
        Amount = amount;
        Direction = direction;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Money received (typically from a customer, spec section 34) - may optionally apply against a specific Invoice.</summary>
public class Receipt : AuditableEntity
{
    public string ReceiptNumber { get; private set; } = string.Empty;
    public DateTime ReceiptDate { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid TreasuryAccountId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public string? PaymentMethod { get; private set; }
    public string? Description { get; private set; }

    private Receipt() { } // EF Core

    public Receipt(string receiptNumber, DateTime receiptDate, Guid treasuryAccountId, decimal amount, string createdBy,
        Guid? customerId = null, Guid? invoiceId = null, string? paymentMethod = null, string? description = null)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        ReceiptNumber = receiptNumber;
        ReceiptDate = receiptDate;
        TreasuryAccountId = treasuryAccountId;
        Amount = amount;
        CustomerId = customerId;
        InvoiceId = invoiceId;
        PaymentMethod = paymentMethod;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Money paid out (supplier, expense, etc. - spec section 34).</summary>
public class Payment : AuditableEntity
{
    public string PaymentNumber { get; private set; } = string.Empty;
    public DateTime PaymentDate { get; private set; }
    public Guid TreasuryAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string PayeeDescription { get; private set; } = string.Empty;
    public string? PaymentMethod { get; private set; }
    public string? Description { get; private set; }

    private Payment() { } // EF Core

    public Payment(string paymentNumber, DateTime paymentDate, Guid treasuryAccountId, decimal amount,
        string payeeDescription, string createdBy, string? paymentMethod = null, string? description = null)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        PaymentNumber = paymentNumber;
        PaymentDate = paymentDate;
        TreasuryAccountId = treasuryAccountId;
        Amount = amount;
        PayeeDescription = payeeDescription;
        PaymentMethod = paymentMethod;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Transfer between two treasury accounts (spec section 34).</summary>
public class TreasuryTransfer : AuditableEntity
{
    public string TransferNumber { get; private set; } = string.Empty;
    public DateTime TransferDate { get; private set; }
    public Guid FromAccountId { get; private set; }
    public Guid ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }

    private TreasuryTransfer() { } // EF Core

    public TreasuryTransfer(string transferNumber, DateTime transferDate, Guid fromAccountId, Guid toAccountId,
        decimal amount, string createdBy, string? description = null)
    {
        if (fromAccountId == toAccountId) throw new ArgumentException("Source and destination accounts must differ.");
        if (amount <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        TransferNumber = transferNumber;
        TransferDate = transferDate;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        Amount = amount;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
