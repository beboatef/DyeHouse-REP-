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
}
