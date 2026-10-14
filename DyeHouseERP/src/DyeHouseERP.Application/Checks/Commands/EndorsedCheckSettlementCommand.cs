using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Checks.Commands;

/// <summary>
/// **An endorsed customer cheque settling a supplier payable** (spec section 39:
/// Customer → Company → Supplier).
///
/// This is the financial half of the endorsement: the physical hand-over of the
/// SAME instrument to the supplier (a CheckMovement) plus the two balance movements
/// it produces, in ONE atomic step. It exists because the alternative - paying the
/// supplier with cash and separately endorsing the cheque - would invent money:
///
///   * **No cash moves.** Nothing passed through our cash/bank account, so NO
///     TreasuryTransaction is written at all. A cheque is not a receipt of cash, and
///     endorsing it is not a payment out of an account (spec section 39).
///
///   * **The supplier's payable goes down** by a Credit SupplierLedgerEntry posted
///     against a real SupplierPayment document (the same document the register and the
///     supplier statement read), carrying the cheque's Id and number so the payment is
///     traceable back to the instrument.
///
///   * **The customer's account goes down** by a Credit CustomerLedgerEntry: their
///     cheque is what settled what they owed us. Without this leg the cheque would
///     vanish from the customer statement while still sitting on their balance.
///
///   * **Duplicate settlement is impossible.** The check/endorsement link is checked
///     under a database application lock taken on the cheque itself (so two concurrent
///     requests cannot both observe "not settled yet"), reinforced by an optimistic
///     concurrency token on the cheque and a filtered UNIQUE index on
///     SupplierPayments.CheckId at the database level.
///
/// Cancelling the resulting payment restores BOTH balances (see
/// CancelSupplierPaymentCommandHandler) - the two accounts move together or not at all.
/// </summary>
public record SettleSupplierPayableWithEndorsedCheckCommand(
    Guid CheckId,
    Guid SupplierId,
    DateTime SettlementDate,
    Guid? SupplierInvoiceId = null,
    string? Reason = null,
    string? PaymentMethod = null) : IRequest<SupplierPaymentDto>;

public class SettleSupplierPayableWithEndorsedCheckCommandValidator
    : AbstractValidator<SettleSupplierPayableWithEndorsedCheckCommand>
{
    public SettleSupplierPayableWithEndorsedCheckCommandValidator()
    {
        RuleFor(x => x.CheckId).NotEmpty();
        RuleFor(x => x.SupplierId).NotEmpty();
    }
}

public class SettleSupplierPayableWithEndorsedCheckCommandHandler
    : IRequestHandler<SettleSupplierPayableWithEndorsedCheckCommand, SupplierPaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _lockService;

    public SettleSupplierPayableWithEndorsedCheckCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IPeriodCloseService periodClose, IAllocationLockService lockService)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
        _periodClose = periodClose; _lockService = lockService;
    }

    public async Task<SupplierPaymentDto> Handle(
        SettleSupplierPayableWithEndorsedCheckCommand request, CancellationToken cancellationToken)
    {
        // Serialize every settlement of THIS cheque: without it, two concurrent requests could both
        // read "no settlement yet" and both post, leaving one instrument settling two payables. The
        // lock is the database application lock (sp_getapplock) the stock paths use, keyed on the
        // cheque, and it is held through the commit below.
        await using var checkLock = await _lockService.AcquireNamedAsync(
            $"Shared:CheckSettlement:{request.CheckId}", cancellationToken);

        // The ledger rows are dated the settlement date, so that date must be inside an open period.
        await using var periodGuard = await _periodClose.BeginPostingAsync(request.SettlementDate, cancellationToken);

        var check = await _db.Checks.Include(c => c.Movements)
            .FirstOrDefaultAsync(c => c.Id == request.CheckId, cancellationToken)
            ?? throw new NotFoundException("Check", request.CheckId);

        if (check.Direction != CheckDirection.CustomerCheck)
            throw new DomainException(
                "Only an incoming customer check can be endorsed onward to settle a supplier payable. " +
                "A check drawn by us is settled by its own clearance.");

        if (check.CustomerId is null)
            throw new DomainException(
                $"Check {check.CheckNumber} is not linked to a customer, so it cannot settle a customer balance.");

        if (check.Status is CheckStatus.Cancelled or CheckStatus.Bounced or CheckStatus.Cleared or CheckStatus.Deposited)
            throw new DomainException(
                $"Check {check.CheckNumber} is {check.Status} and cannot be endorsed to a supplier anymore.");

        if (check.Status == CheckStatus.Endorsed && check.SupplierId != request.SupplierId)
            throw new DomainException(
                $"Check {check.CheckNumber} is already endorsed to another supplier - one instrument settles one payable.");

        // Duplicate settlement guard. The lock above makes this read-then-write safe against a
        // concurrent request; the unique filtered index on SupplierPayments.CheckId is the
        // database-level backstop for anything that bypasses this handler.
        var existingSettlement = await _db.SupplierPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CheckId == check.Id && p.Status != TreasuryDocumentStatus.Cancelled, cancellationToken);
        if (existingSettlement is not null)
            throw new DomainException(
                $"Check {check.CheckNumber} has already settled supplier payment {existingSettlement.PaymentNumber}. " +
                "Cancel that settlement first - a cheque is never counted twice.");

        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        if (request.SupplierInvoiceId.HasValue)
        {
            var invoice = await _db.SupplierInvoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == request.SupplierInvoiceId.Value, cancellationToken)
                ?? throw new NotFoundException("SupplierInvoice", request.SupplierInvoiceId.Value);

            if (invoice.SupplierId != supplier.Id)
                throw new DomainException(
                    $"Supplier invoice {invoice.InvoiceNumber} belongs to a different supplier.");

            if (invoice.Status != SupplierInvoiceStatus.Posted)
                throw new DomainException(
                    $"Supplier invoice {invoice.InvoiceNumber} is {invoice.Status}; only a posted invoice has a payable to settle.");
        }

        // The endorsement itself, in the same transaction, when the instrument has not been handed
        // over yet: one document trail (received → endorsed) and one financial event, never a cheque
        // that is financially settled while still sitting in our drawer.
        var movementsBefore = CheckWorkflow.SnapshotMovements(check);
        if (check.Status != CheckStatus.Endorsed)
            check.EndorseToSupplier(supplier.Id, supplier.Name, request.SettlementDate, _currentUser.UserName, request.Reason);
        CheckWorkflow.RegisterNewMovements(_db, check, movementsBefore);

        var paymentNumber = await _numberGenerator.NextAsync(DocumentType.SupplierPayment, cancellationToken: cancellationToken);

        // treasuryAccountId: null - an endorsed-cheque settlement deliberately has no account, because
        // no money left one. The domain allows that exactly for a cheque-settled payment.
        var payment = new SupplierPayment(
            paymentNumber, request.SettlementDate, supplier.Id, treasuryAccountId: null, check.Amount,
            _currentUser.UserName, request.PaymentMethod ?? "Endorsed cheque",
            checkId: check.Id, checkNumber: check.CheckNumber, supplierInvoiceId: request.SupplierInvoiceId,
            description: request.Reason
                ?? $"Settlement of supplier payable by endorsed customer check {check.CheckNumber}");

        _db.SupplierPayments.Add(payment);

        // NO TreasuryTransaction here, on purpose: the instrument went to the supplier and never
        // passed through our cash/bank account. Writing money in or out would be fictitious.

        // Payable reduction on the supplier's own account.
        _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry(
            supplier.Id, request.SettlementDate, DocumentType.SupplierPayment, paymentNumber, payment.Id,
            debit: 0m, credit: check.Amount,
            description: $"Settlement by customer check {check.CheckNumber}",
            createdBy: _currentUser.UserName));

        // And the customer's account: their cheque is what settled what they owed us. The row points at
        // the cheque document, and the cheque points at this payment, so the whole chain is walkable:
        // customer cheque ↔ endorsement movement ↔ supplier payment ↔ both balances.
        _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            check.CustomerId.Value, request.SettlementDate, DocumentType.CheckRegister, check.CheckNumber, check.Id,
            debit: 0m, credit: check.Amount,
            description: $"Customer check {check.CheckNumber} endorsed to supplier {supplier.Name}",
            createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierPaymentDto
        {
            Id = payment.Id,
            PaymentNumber = payment.PaymentNumber,
            PaymentDate = payment.PaymentDate,
            SupplierId = payment.SupplierId,
            SupplierCode = supplier.Code,
            SupplierName = supplier.Name,
            TreasuryAccountId = null,
            TreasuryAccountName = null,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PaymentMethod = payment.PaymentMethod,
            CheckId = payment.CheckId,
            CheckNumber = payment.CheckNumber,
            SupplierInvoiceId = payment.SupplierInvoiceId,
            Description = payment.Description,
            CreatedBy = payment.CreatedBy,
            CreatedAtUtc = payment.CreatedAtUtc
        };
    }
}
