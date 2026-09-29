using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Commands;

/// <summary>
/// Records money received (typically from a customer, spec section 34).
/// Posts a Credit to the customer's statement when CustomerId is set, and
/// - if it targets a specific Invoice - re-evaluates that invoice's status
/// against total payments received so far (spec section 32: Draft/Issued/
/// PartiallyPaid/Paid).
/// </summary>
public record CreateReceiptCommand(
    DateTime ReceiptDate, Guid TreasuryAccountId, decimal Amount, Guid? CustomerId, Guid? InvoiceId,
    string? PaymentMethod, string? Description) : IRequest<ReceiptDto>;

public class CreateReceiptCommandValidator : AbstractValidator<CreateReceiptCommand>
{
    public CreateReceiptCommandValidator()
    {
        RuleFor(x => x.TreasuryAccountId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public class CreateReceiptCommandHandler : IRequestHandler<CreateReceiptCommand, ReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _documentLock;

    public CreateReceiptCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose, IAllocationLockService documentLock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
        _periodClose = periodClose; _documentLock = documentLock;
    }

    public async Task<ReceiptDto> Handle(CreateReceiptCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(request.ReceiptDate, cancellationToken);

        var account = await _db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == request.TreasuryAccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId);

        // ------------------------------------------------------------------
        // Validation happens BEFORE anything is staged for saving: no receipt,
        // no treasury row and no customer-ledger row is created until the
        // target invoice is proven to exist, be payable, and belong to the
        // customer named in the request.
        // ------------------------------------------------------------------
        Invoice? invoice = null;
        // R5: the effective customer - the request's, or the invoice's when the
        // request omitted it.
        var customerId = request.CustomerId;
        if (request.InvoiceId.HasValue)
        {
            invoice = await _db.Invoices.Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice", request.InvoiceId.Value);

            // B5: the target invoice must be live. A cancelled or draft invoice
            // cannot legitimately receive money against it.
            if (invoice.Status == InvoiceStatus.Cancelled)
                throw new DomainException($"Invoice {invoice.InvoiceNumber} is cancelled and cannot receive payments.");
            if (invoice.Status == InvoiceStatus.Draft)
                throw new DomainException($"Invoice {invoice.InvoiceNumber} is still a draft - issue it before recording receipts against it.");

            // H4 ownership check: money received against an invoice may only be
            // credited to THAT invoice's customer. Without this, a receipt could
            // post a credit to one customer's statement while settling another
            // customer's invoice - the ledger and the invoice would disagree.
            if (request.CustomerId.HasValue && request.CustomerId.Value != invoice.CustomerId)
                throw new DomainException(
                    $"Customer does not match invoice {invoice.InvoiceNumber}. The receipt names a different customer, " +
                    "so the payment would post to the wrong statement.");

            // R5: when the request names no customer but does name an invoice,
            // the invoice IS the authority on who the money belongs to. Inferring
            // it here means the receipt is always credited to the right
            // statement instead of silently posting with no customer ledger leg
            // at all (which would leave the invoice settled but the customer's
            // statement untouched).
            customerId = invoice.CustomerId;
        }

        // H4 race guard: two receipts for the SAME invoice both read the same
        // "already paid" sum and would both pass the overpayment guard, letting
        // the pair collectively overpay. Serializing on the invoice makes the
        // second request re-read the first one's committed rows.
        if (request.InvoiceId.HasValue)
        {
            await using (await _documentLock.AcquireNamedAsync($"Invoice:{request.InvoiceId.Value}", cancellationToken))
            {
                return await PostAsync(request, account, invoice, customerId, cancellationToken);
            }
        }

        return await PostAsync(request, account, invoice, customerId, cancellationToken);
    }

    private async Task<ReceiptDto> PostAsync(
        CreateReceiptCommand request, TreasuryAccount account, Invoice? invoice, Guid? customerId,
        CancellationToken cancellationToken)
    {
        var receiptNumber = await _numberGenerator.NextAsync(DocumentType.Receipt, cancellationToken: cancellationToken);

        var receipt = new Receipt(receiptNumber, request.ReceiptDate, request.TreasuryAccountId, request.Amount,
            _currentUser.UserName, customerId, request.InvoiceId, request.PaymentMethod, request.Description);
        _db.Receipts.Add(receipt);

        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.TreasuryAccountId, request.ReceiptDate, DocumentType.Receipt, receiptNumber, receipt.Id,
            request.Amount, TreasuryDirection.In, request.Description, _currentUser.UserName));

        if (customerId.HasValue)
        {
            _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                customerId.Value, request.ReceiptDate, DocumentType.Receipt, receiptNumber, receipt.Id,
                debit: 0, credit: request.Amount, description: $"Receipt {receiptNumber}", createdBy: _currentUser.UserName));
        }

        if (invoice is not null)
        {
            // B5 overpayment rule: the system has no customer-advance model,
            // so a receipt against a live invoice may not exceed that
            // invoice's total minus what previous live receipts already paid.
            // Re-read here, INSIDE the invoice lock, so the sum includes any
            // receipt that committed while this request was queueing.
            var priorLiveReceipts = await _db.Receipts
                .Where(r => r.InvoiceId == invoice.Id && r.Status != TreasuryDocumentStatus.Cancelled)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0;
            var totalPaid = priorLiveReceipts + request.Amount;

            var lines = invoice.Lines.ToList();
            var invoiceTotal = lines.Sum(l => l.Quantity * l.ProcessingPrice) - invoice.Discount + invoice.Tax;
            if (totalPaid > invoiceTotal)
                throw new DomainException(
                    $"Receipt of {totalPaid:0.##} would exceed invoice {invoice.InvoiceNumber} total of {invoiceTotal:0.##} " +
                    $"(already paid {priorLiveReceipts:0.##}). Overpayments are not supported; record the exact due amount.");

            invoice.ApplyPayment(totalPaid);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ReceiptDto
        {
            Id = receipt.Id, ReceiptNumber = receipt.ReceiptNumber, ReceiptDate = receipt.ReceiptDate,
            CustomerId = receipt.CustomerId, TreasuryAccountId = account.Id, TreasuryAccountName = account.Name,
            InvoiceId = receipt.InvoiceId, InvoiceNumber = invoice?.InvoiceNumber,
            Amount = receipt.Amount, PaymentMethod = receipt.PaymentMethod, Description = receipt.Description,
            Status = receipt.Status.ToString()
        };
    }
}
