using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A payment made to a supplier (spec sections 35 and 41). This is a real document
/// with a system-generated number, not a bare ledger row, so every payment is
/// traceable to who was paid, from which account, when, by whom, and - where
/// applicable - to the endorsed check it was settled with (spec sections 39-40).
///
/// Posting writes exactly two things: an OUT TreasuryTransaction for the cash/bank
/// movement and a Credit SupplierLedgerEntry for the payable reduction.
/// </summary>
public class SupplierPayment : AuditableEntity
{
    public string PaymentNumber { get; private set; } = string.Empty; // system-generated, e.g. "SPAY-2026-000007"
    public DateTime PaymentDate { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid TreasuryAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";

    /// <summary>Cash, bank transfer, cheque, ... free text because payment methods differ per factory.</summary>
    public string? PaymentMethod { get; private set; }

    /// <summary>Set when the payment was settled by endorsing a customer check to the supplier (spec section 39).</summary>
    public Guid? CheckId { get; private set; }
    public string? CheckNumber { get; private set; }

    public Guid? SupplierInvoiceId { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Posted or Cancelled (B4/B5) - a cancelled payment keeps its row with linked reversal entries; never deleted.</summary>
    public TreasuryDocumentStatus Status { get; private set; } = TreasuryDocumentStatus.Posted;

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. A supplier
    /// payment moves treasury out and credits the supplier ledger; B4/B5
    /// require that a posted payment cannot be cancelled twice, and this is the
    /// database-level guarantee behind that rule.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private SupplierPayment() { } // EF Core

    public SupplierPayment(string paymentNumber, DateTime paymentDate, Guid supplierId, Guid treasuryAccountId,
        decimal amount, string createdBy, string? paymentMethod = null, Guid? checkId = null,
        string? checkNumber = null, Guid? supplierInvoiceId = null, string? description = null, string currency = "EGP")
    {
        if (string.IsNullOrWhiteSpace(paymentNumber))
            throw new ArgumentException("Payment number is required.", nameof(paymentNumber));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier is required.", nameof(supplierId));
        if (treasuryAccountId == Guid.Empty)
            throw new DomainException("A treasury/bank account is required for a supplier payment.");
        if (amount <= 0) throw new DomainException("A supplier payment amount must be greater than zero.");

        PaymentNumber = paymentNumber;
        PaymentDate = paymentDate;
        SupplierId = supplierId;
        TreasuryAccountId = treasuryAccountId;
        Amount = amount;
        PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod.Trim();
        CheckId = checkId;
        CheckNumber = checkNumber;
        SupplierInvoiceId = supplierInvoiceId;
        Description = description;
        Currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency.Trim().ToUpperInvariant();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Flips to Cancelled with the reason recorded (Invoice.Cancel convention); reversal ledger rows are written by the handler.</summary>
    public void Cancel(string reason, string cancelledBy)
    {
        if (Status == TreasuryDocumentStatus.Cancelled)
            throw new DomainException($"Supplier payment '{PaymentNumber}' is already cancelled.");

        Status = TreasuryDocumentStatus.Cancelled;
        Description = string.IsNullOrWhiteSpace(Description) ? $"[Cancelled] {reason}" : $"{Description}\n[Cancelled] {reason}";
        ModifiedBy = cancelledBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
