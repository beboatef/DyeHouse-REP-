using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Invoices.Commands;

// ============================================================ header
/// <summary>
/// Edits the header of a DRAFT invoice (spec section 32). A draft has posted nothing - no
/// customer-ledger row and no delivery link - so its date, delivery link, discount, tax and notes
/// are ordinary working data. Editing is refused the moment the invoice is Issued: from then on it
/// is financially real and is corrected only through the cancellation/reversal workflow.
///
/// The permission is the dedicated, configurable <c>invoices.edit</c> right (not the admin role),
/// so a non-admin accountant can be granted it - or not.
/// </summary>
public record UpdateInvoiceCommand(
    Guid Id, DateTime InvoiceDate, Guid? DeliveryId, decimal Discount, decimal Tax, string? Notes)
    : IRequest<InvoiceDto>;

public class UpdateInvoiceCommandValidator : AbstractValidator<UpdateInvoiceCommand>
{
    public UpdateInvoiceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tax).GreaterThanOrEqualTo(0);
    }
}

public class UpdateInvoiceCommandHandler : IRequestHandler<UpdateInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<InvoiceDto> Handle(UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.Id);

        invoice.UpdateHeader(request.InvoiceDate, request.DeliveryId, request.Discount, request.Tax,
            request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}

// ============================================================ add line
public record AddInvoiceLineCommand(
    Guid InvoiceId, Guid? ProductionOrderId, Guid ItemId, string? Color,
    decimal Quantity, decimal ProcessingPrice, UnitOfMeasure Unit, string? Notes,
    Guid? CostEntryId = null) : IRequest<InvoiceDto>;

public class AddInvoiceLineCommandValidator : AbstractValidator<AddInvoiceLineCommand>
{
    public AddInvoiceLineCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ProcessingPrice).GreaterThanOrEqualTo(0);
        // A line is billed in the unit its SERVICE price is configured in, so the unit must be a real one.
        RuleFor(x => x.Unit).IsInEnum();
    }
}

public class AddInvoiceLineCommandHandler : IRequestHandler<AddInvoiceLineCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AddInvoiceLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<InvoiceDto> Handle(AddInvoiceLineCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        // Checked BEFORE the line is appended: the billed unit must follow the order's configured
        // service price, the quantity must be the approved quantity in that unit (never a conversion),
        // and a billed cost must be explicitly chargeable and not already billed elsewhere.
        await InvoiceBillingGuard.EnsureBillableAsync(
            _db, request.ProductionOrderId, request.ItemId, request.Unit, request.Quantity,
            request.CostEntryId, excludeInvoiceLineId: null, cancellationToken);

        var line = invoice.AddLine(request.ProductionOrderId, request.ItemId, request.Color,
            request.Quantity, request.ProcessingPrice, request.Unit, request.Notes, request.CostEntryId);

        // The invoice is already persisted, so EF would take the new line for an existing row and save an
        // UPDATE that hits nothing. Registering it explicitly makes SaveChangesAsync INSERT it - the same
        // rule a new purchase-order line or raw allocation follows.
        _db.InvoiceLines.Add(line);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}

// ============================================================ update line
public record UpdateInvoiceLineCommand(
    Guid InvoiceId, Guid LineId, decimal Quantity, decimal ProcessingPrice, UnitOfMeasure Unit,
    string? Color, string? Notes) : IRequest<InvoiceDto>;

public class UpdateInvoiceLineCommandValidator : AbstractValidator<UpdateInvoiceLineCommand>
{
    public UpdateInvoiceLineCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ProcessingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Unit).IsInEnum();
    }
}

public class UpdateInvoiceLineCommandHandler : IRequestHandler<UpdateInvoiceLineCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateInvoiceLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<InvoiceDto> Handle(UpdateInvoiceLineCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        var editing = invoice.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new NotFoundException("InvoiceLine", request.LineId);

        // The edited line's OWN previous figures are excluded from the already-billed sum, so a line
        // can be corrected against its own order without being counted against itself - while every
        // other live line still counts, so a correction can never over-bill the approved quantity.
        await InvoiceBillingGuard.EnsureBillableAsync(
            _db, editing.ProductionOrderId, editing.ItemId, request.Unit, request.Quantity,
            editing.CostEntryId, excludeInvoiceLineId: request.LineId, cancellationToken);

        invoice.UpdateLine(request.LineId, request.Quantity, request.ProcessingPrice, request.Unit, request.Color,
            request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}

// ============================================================ remove line
public record RemoveInvoiceLineCommand(Guid InvoiceId, Guid LineId) : IRequest<InvoiceDto>;

public class RemoveInvoiceLineCommandValidator : AbstractValidator<RemoveInvoiceLineCommand>
{
    public RemoveInvoiceLineCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
    }
}

public class RemoveInvoiceLineCommandHandler : IRequestHandler<RemoveInvoiceLineCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RemoveInvoiceLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<InvoiceDto> Handle(RemoveInvoiceLineCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        invoice.RemoveLine(request.LineId, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetInvoicesQueryHandler.LoadDtoAsync(_db, invoice.Id, cancellationToken);
    }
}
