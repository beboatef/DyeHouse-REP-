using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Commands;

// ============================================================ Receipt cancel
/// <summary>
/// Cancels a posted Receipt (B4): the receipt row flips to Cancelled (the
/// original is never edited beyond the status/notes, and never deleted), and
/// exactly one opposite TreasuryTransaction row is added, linked to the
/// original via ReversesTransactionId - the same append-only reversal
/// pattern as delivery/ready-goods cancellation. When the receipt was applied
/// to an invoice, an equal-and-opposite CustomerLedgerEntry credit is also
/// written so the customer statement returns to its pre-receipt position
/// without double-counting, and the invoice's paid-status is re-evaluated.
/// </summary>
public record CancelReceiptCommand(Guid ReceiptId, string Reason) : IRequest<ReceiptDto>;

public class CancelReceiptCommandValidator : AbstractValidator<CancelReceiptCommand>
{
    public CancelReceiptCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

public class CancelReceiptCommandHandler : IRequestHandler<CancelReceiptCommand, ReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public CancelReceiptCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose;
    }

    public async Task<ReceiptDto> Handle(CancelReceiptCommand request, CancellationToken cancellationToken)
    {
        // Reversal rows are dated today - a closed period blocks only if today
        // itself is closed (same convention as every other reversal).
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == request.ReceiptId, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException("Receipt", request.ReceiptId);

        // Double-reversal guard: the entity refuses a second Cancel.
        receipt.Cancel(request.Reason, _currentUser.UserName);

        var originals = await _db.TreasuryTransactions
            .Where(t => t.SourceDocumentId == receipt.Id && t.SourceDocumentType == DocumentType.Receipt)
            .ToListAsync(cancellationToken);

        foreach (var row in originals)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                row.TreasuryAccountId, _clock.UtcNow, DocumentType.Receipt, receipt.ReceiptNumber, receipt.Id,
                row.Amount, row.Direction == TreasuryDirection.In ? TreasuryDirection.Out : TreasuryDirection.In,
                $"Cancellation of receipt {receipt.ReceiptNumber}: {request.Reason}",
                _currentUser.UserName, reversesTransactionId: row.Id));
        }

        // Keep the customer statement consistent: reverse the receipt's credit.
        if (receipt.CustomerId.HasValue)
        {
            _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                receipt.CustomerId.Value, _clock.UtcNow, DocumentType.Receipt, receipt.ReceiptNumber, receipt.Id,
                debit: receipt.Amount, credit: 0,
                description: $"Cancellation of receipt {receipt.ReceiptNumber}: {request.Reason}",
                createdBy: _currentUser.UserName));

            if (receipt.InvoiceId.HasValue)
            {
                var invoice = await _db.Invoices.Include(i => i.Lines)
                    .FirstOrDefaultAsync(i => i.Id == receipt.InvoiceId.Value, cancellationToken);
                if (invoice is not null && invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled))
                {
                    var remaining = await _db.Receipts
                        .Where(r => r.InvoiceId == invoice.Id && r.Id != receipt.Id && r.Status != TreasuryDocumentStatus.Cancelled)
                        .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0;
                    invoice.ApplyPayment(remaining);
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await LoadReceiptDtoAsync(_db, receipt.Id, cancellationToken);
    }

    internal static async Task<ReceiptDto> LoadReceiptDtoAsync(IApplicationDbContext db, Guid id, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var account = await db.TreasuryAccounts.AsNoTracking().FirstAsync(a => a.Id == receipt.TreasuryAccountId, ct);
        string? invoiceNumber = null;
        if (receipt.InvoiceId.HasValue)
            invoiceNumber = await db.Invoices.AsNoTracking()
                .Where(i => i.Id == receipt.InvoiceId.Value).Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(ct);

        return new ReceiptDto
        {
            Id = receipt.Id, ReceiptNumber = receipt.ReceiptNumber, ReceiptDate = receipt.ReceiptDate,
            CustomerId = receipt.CustomerId, TreasuryAccountId = receipt.TreasuryAccountId,
            TreasuryAccountName = account.Name, InvoiceId = receipt.InvoiceId, InvoiceNumber = invoiceNumber,
            Amount = receipt.Amount, PaymentMethod = receipt.PaymentMethod, Description = receipt.Description,
            Status = receipt.Status.ToString()
        };
    }
}

// ============================================================ Payment cancel
/// <summary>
/// Cancels a posted Payment (B4): same reversal mechanics as the receipt -
/// status flips, one opposite TreasuryTransaction per original row linked by
/// ReversesTransactionId, original rows untouched. When the payment was
/// linked to a supplier invoice, the supplier ledger gets the balancing
/// Debit row so the payable returns to its pre-payment state.
/// </summary>
public record CancelPaymentCommand(Guid PaymentId, string Reason) : IRequest<PaymentDto>;

public class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

public class CancelPaymentCommandHandler : IRequestHandler<CancelPaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public CancelPaymentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose;
    }

    public async Task<PaymentDto> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException("Payment", request.PaymentId);

        payment.Cancel(request.Reason, _currentUser.UserName);

        var originals = await _db.TreasuryTransactions
            .Where(t => t.SourceDocumentId == payment.Id && t.SourceDocumentType == DocumentType.Payment)
            .ToListAsync(cancellationToken);

        foreach (var row in originals)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                row.TreasuryAccountId, _clock.UtcNow, DocumentType.Payment, payment.PaymentNumber, payment.Id,
                row.Amount, row.Direction == TreasuryDirection.In ? TreasuryDirection.Out : TreasuryDirection.In,
                $"Cancellation of payment {payment.PaymentNumber}: {request.Reason}",
                _currentUser.UserName, reversesTransactionId: row.Id));
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await LoadPaymentDtoAsync(_db, payment.Id, cancellationToken);
    }

    internal static async Task<PaymentDto> LoadPaymentDtoAsync(IApplicationDbContext db, Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == id, ct);
        var account = await db.TreasuryAccounts.AsNoTracking().FirstAsync(a => a.Id == payment.TreasuryAccountId, ct);

        return new PaymentDto
        {
            Id = payment.Id, PaymentNumber = payment.PaymentNumber, PaymentDate = payment.PaymentDate,
            TreasuryAccountId = payment.TreasuryAccountId, TreasuryAccountName = account.Name,
            Amount = payment.Amount, PayeeDescription = payment.PayeeDescription, Description = payment.Description,
            Status = payment.Status.ToString()
        };
    }
}
