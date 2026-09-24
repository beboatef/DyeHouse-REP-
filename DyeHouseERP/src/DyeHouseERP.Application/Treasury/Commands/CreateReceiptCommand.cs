using DyeHouseERP.Application.Common.Exceptions;
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

    public CreateReceiptCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _periodClose = periodClose;
    }

    public async Task<ReceiptDto> Handle(CreateReceiptCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(request.ReceiptDate, cancellationToken);

        var account = await _db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == request.TreasuryAccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId);

        var receiptNumber = await _numberGenerator.NextAsync(DocumentType.Receipt, cancellationToken: cancellationToken);

        var receipt = new Receipt(receiptNumber, request.ReceiptDate, request.TreasuryAccountId, request.Amount,
            _currentUser.UserName, request.CustomerId, request.InvoiceId, request.PaymentMethod, request.Description);
        _db.Receipts.Add(receipt);

        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.TreasuryAccountId, request.ReceiptDate, DocumentType.Receipt, receiptNumber, receipt.Id,
            request.Amount, TreasuryDirection.In, request.Description, _currentUser.UserName));

        Invoice? invoice = null;
        if (request.CustomerId.HasValue)
        {
            _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                request.CustomerId.Value, request.ReceiptDate, DocumentType.Receipt, receiptNumber, receipt.Id,
                debit: 0, credit: request.Amount, description: $"Receipt {receiptNumber}", createdBy: _currentUser.UserName));
        }

        if (request.InvoiceId.HasValue)
        {
            invoice = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice", request.InvoiceId.Value);

            var priorReceipts = await _db.Receipts.Where(r => r.InvoiceId == request.InvoiceId).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0;
            var totalPaid = priorReceipts + request.Amount;
            invoice.ApplyPayment(totalPaid);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ReceiptDto
        {
            Id = receipt.Id, ReceiptNumber = receipt.ReceiptNumber, ReceiptDate = receipt.ReceiptDate,
            CustomerId = receipt.CustomerId, TreasuryAccountId = account.Id, TreasuryAccountName = account.Name,
            InvoiceId = receipt.InvoiceId, InvoiceNumber = invoice?.InvoiceNumber,
            Amount = receipt.Amount, PaymentMethod = receipt.PaymentMethod, Description = receipt.Description
        };
    }
}
