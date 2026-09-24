using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Append-only financial ledger row backing the customer statement (spec
/// section 33). An Issued Invoice posts a Debit; a Receipt posts a Credit.
/// Balance is always SUM(Debit) - SUM(Credit), never a stored running total.
/// </summary>
public class CustomerLedgerEntry : BaseEntity
{
    public Guid CustomerId { get; private set; }
    public DateTime EntryDate { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public string SourceDocumentNumber { get; private set; } = string.Empty;
    public Guid SourceDocumentId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private CustomerLedgerEntry() { } // EF Core

    public CustomerLedgerEntry(
        Guid customerId, DateTime entryDate, DocumentType sourceDocumentType, string sourceDocumentNumber,
        Guid sourceDocumentId, decimal debit, decimal credit, string description, string createdBy)
    {
        CustomerId = customerId;
        EntryDate = entryDate;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentNumber = sourceDocumentNumber;
        SourceDocumentId = sourceDocumentId;
        Debit = debit;
        Credit = credit;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
