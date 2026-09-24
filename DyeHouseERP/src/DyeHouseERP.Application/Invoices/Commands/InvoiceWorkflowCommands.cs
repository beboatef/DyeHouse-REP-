using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Invoices.Commands;

// -------------------- Issue --------------------
public record IssueInvoiceCommand(Guid InvoiceId) : IRequest<InvoiceDto>;

/// <summary>Issuing an invoice posts a Debit to the customer's statement (spec sections 32-33) - the invoice becomes financially real at this point, not at Draft.</summary>
public class IssueInvoiceCommandHandler : IRequestHandler<IssueInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public IssueInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose;
    }

    public async Task<InvoiceDto> Handle(IssueInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        await _periodClose.EnsureOpenAsync(invoice.InvoiceDate, cancellationToken);
        var lines = await _db.InvoiceLines.Where(l => l.InvoiceId == invoice.Id).ToListAsync(cancellationToken);

        invoice.Issue();

        var subTotal = lines.Sum(l => l.Quantity * l.ProcessingPrice);
        var total = subTotal - invoice.Discount + invoice.Tax;

        _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            invoice.CustomerId, _clock.UtcNow, DocumentType.Invoice, invoice.InvoiceNumber, invoice.Id,
            debit: total, credit: 0, description: $"Invoice {invoice.InvoiceNumber}", createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}

// -------------------- Cancel --------------------
public record CancelInvoiceCommand(Guid InvoiceId, string Reason) : IRequest<InvoiceDto>;

public class CancelInvoiceCommandValidator : AbstractValidator<CancelInvoiceCommand>
{
    public CancelInvoiceCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

/// <summary>Cancelling an Issued invoice reverses its Debit with an equal Credit - the original row is never edited or deleted (spec section 32-33).</summary>
public class CancelInvoiceCommandHandler : IRequestHandler<CancelInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public CancelInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<InvoiceDto> Handle(CancelInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        if (invoice.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid)
        {
            var lines = await _db.InvoiceLines.Where(l => l.InvoiceId == invoice.Id).ToListAsync(cancellationToken);
            var subTotal = lines.Sum(l => l.Quantity * l.ProcessingPrice);
            var total = subTotal - invoice.Discount + invoice.Tax;

            _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                invoice.CustomerId, _clock.UtcNow, DocumentType.Invoice, invoice.InvoiceNumber, invoice.Id,
                debit: 0, credit: total, description: $"Cancellation of invoice {invoice.InvoiceNumber}", createdBy: _currentUser.UserName));
        }

        invoice.Cancel(request.Reason, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}
