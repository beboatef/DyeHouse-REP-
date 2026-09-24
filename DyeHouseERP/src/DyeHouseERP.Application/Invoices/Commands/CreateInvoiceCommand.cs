using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.Invoices.Commands;

public record CreateInvoiceCommand(
    Guid CustomerId, DateTime InvoiceDate, Guid? DeliveryId, decimal Discount, decimal Tax,
    string? Notes, List<InvoiceLineInput> Lines) : IRequest<InvoiceDto>;

public class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.ProcessingPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<InvoiceDto> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoiceNumber = await _numberGenerator.NextAsync(DocumentType.Invoice, cancellationToken: cancellationToken);

        var invoice = new Invoice(invoiceNumber, request.InvoiceDate, request.CustomerId, _currentUser.UserName,
            request.DeliveryId, request.Discount, request.Tax, request.Notes);

        foreach (var line in request.Lines)
            invoice.AddLine(line.ProductionOrderId, line.ItemId, line.Color, line.Quantity, line.ProcessingPrice, line.Notes);

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}
