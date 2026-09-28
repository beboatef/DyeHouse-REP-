using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Application.Purchases.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Purchases.Commands;

public class SupplierInvoiceLineInput
{
    public Guid? MaterialId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Records a supplier's own invoice (spec sections 35 and 41). It starts as a
/// draft: posting it is the single moment a payable appears on the supplier
/// account, which is what prevents a purchase order + goods receipt from creating
/// a double-counted liability.
/// </summary>
public record CreateSupplierInvoiceCommand(
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime DueDate,
    Guid SupplierId,
    Guid? PurchaseOrderId = null,
    Guid? PurchaseReceiptId = null,
    string? InternalNumber = null,
    string? Notes = null,
    string Currency = "EGP",
    List<SupplierInvoiceLineInput>? Lines = null,
    decimal Discount = 0,
    decimal Tax = 0) : IRequest<SupplierInvoiceDto>;

public class CreateSupplierInvoiceCommandValidator : AbstractValidator<CreateSupplierInvoiceCommand>
{
    public CreateSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(60);
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tax).GreaterThanOrEqualTo(0);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty().MaximumLength(300);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateSupplierInvoiceCommandHandler : IRequestHandler<CreateSupplierInvoiceCommand, SupplierInvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CreateSupplierInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<SupplierInvoiceDto> Handle(CreateSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        await PurchaseRules.EnsureSupplierExists(_db, request.SupplierId, cancellationToken);

        var invoiceNumber = request.InvoiceNumber.Trim();
        var duplicate = await _db.SupplierInvoices.AnyAsync(
            i => i.SupplierId == request.SupplierId && i.InvoiceNumber == invoiceNumber, cancellationToken);
        if (duplicate)
            throw new DuplicateCodeException("SupplierInvoice", $"{request.SupplierId}/{invoiceNumber}");

        if (request.PurchaseOrderId.HasValue &&
            !await _db.PurchaseOrders.AnyAsync(o => o.Id == request.PurchaseOrderId.Value, cancellationToken))
            throw new NotFoundException("PurchaseOrder", request.PurchaseOrderId.Value);

        if (request.PurchaseReceiptId.HasValue &&
            !await _db.PurchaseReceipts.AnyAsync(r => r.Id == request.PurchaseReceiptId.Value, cancellationToken))
            throw new NotFoundException("PurchaseReceipt", request.PurchaseReceiptId.Value);

        var invoice = new SupplierInvoice(invoiceNumber, request.InvoiceDate, request.DueDate, request.SupplierId,
            _currentUser.UserName, request.PurchaseOrderId, request.PurchaseReceiptId,
            request.InternalNumber, request.Notes, request.Currency);

        foreach (var line in request.Lines ?? new List<SupplierInvoiceLineInput>())
            invoice.AddLine(line.MaterialId ?? Guid.Empty, line.Description, line.Quantity, line.Unit, line.UnitPrice);

        // Discount/tax are applied through the domain so the total stays derived
        // from the lines and can never drift from them (spec sections 41 and 55).
        if (request.Discount > 0 || request.Tax > 0)
            invoice.UpdateHeader(request.InvoiceDate, request.DueDate, request.Notes, request.Discount, request.Tax, _currentUser.UserName);

        _db.SupplierInvoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetSupplierInvoiceByIdQuery(invoice.Id), cancellationToken);
    }
}

public record UpdateSupplierInvoiceCommand(
    Guid Id, DateTime InvoiceDate, DateTime DueDate, decimal Discount, decimal Tax, string? Notes = null)
    : IRequest<SupplierInvoiceDto>;

public class UpdateSupplierInvoiceCommandHandler : IRequestHandler<UpdateSupplierInvoiceCommand, SupplierInvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public UpdateSupplierInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<SupplierInvoiceDto> Handle(UpdateSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplierInvoice", request.Id);

        invoice.UpdateHeader(request.InvoiceDate, request.DueDate, request.Notes, request.Discount, request.Tax, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetSupplierInvoiceByIdQuery(invoice.Id), cancellationToken);
    }
}

/// <summary>Posts the invoice: writes exactly one Debit supplier-ledger row for the total (spec sections 41 and 46).</summary>
public record PostSupplierInvoiceCommand(Guid Id) : IRequest<SupplierInvoiceDto>;

public class PostSupplierInvoiceCommandHandler : IRequestHandler<PostSupplierInvoiceCommand, SupplierInvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;
    private readonly ISender _mediator;

    public PostSupplierInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose, ISender mediator)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; _mediator = mediator; }

    public async Task<SupplierInvoiceDto> Handle(PostSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _db.SupplierInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplierInvoice", request.Id);

        await _periodClose.EnsureOpenAsync(invoice.InvoiceDate, cancellationToken);

        invoice.Post(_currentUser.UserName);

        _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry(
            invoice.SupplierId, invoice.InvoiceDate, DocumentType.SupplierInvoice, invoice.InvoiceNumber,
            invoice.Id, debit: invoice.Total, credit: 0m,
            description: $"Supplier invoice {invoice.InvoiceNumber}", createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetSupplierInvoiceByIdQuery(invoice.Id), cancellationToken);
    }
}

/// <summary>
/// Cancels a posted invoice by posting the equal-and-opposite Credit row.
/// Nothing is ever deleted or mutated after posting (spec sections 41 and 46).
/// </summary>
public record CancelSupplierInvoiceCommand(Guid Id, string Reason) : IRequest<SupplierInvoiceDto>;

public class CancelSupplierInvoiceCommandValidator : AbstractValidator<CancelSupplierInvoiceCommand>
{
    public CancelSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CancelSupplierInvoiceCommandHandler : IRequestHandler<CancelSupplierInvoiceCommand, SupplierInvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;
    private readonly IPeriodCloseService _periodClose;

    public CancelSupplierInvoiceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _mediator = mediator; _periodClose = periodClose; }

    public async Task<SupplierInvoiceDto> Handle(CancelSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal row is dated today, so a closed period only
        // blocks this if today is closed - historical corrections stay possible.
        await _periodClose.EnsureOpenAsync(DateTime.UtcNow.Date, cancellationToken);

        var invoice = await _db.SupplierInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplierInvoice", request.Id);

        if (invoice.Status == SupplierInvoiceStatus.Cancelled)
            throw new DomainException("This supplier invoice is already cancelled.");

        var wasPosted = invoice.Status == SupplierInvoiceStatus.Posted;
        var total = invoice.Total;

        invoice.Cancel(_currentUser.UserName, request.Reason);

        if (wasPosted)
        {
            _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry(
                invoice.SupplierId, DateTime.UtcNow.Date, DocumentType.SupplierInvoice,
                $"{invoice.InvoiceNumber}-CANCEL", invoice.Id, debit: 0m, credit: total,
                description: $"Cancellation of supplier invoice {invoice.InvoiceNumber}: {request.Reason}",
                createdBy: _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetSupplierInvoiceByIdQuery(invoice.Id), cancellationToken);
    }
}

/// <summary>One payment made to a supplier (spec sections 35 and 41).</summary>
public class SupplierPaymentDto
{
    public Guid Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid TreasuryAccountId { get; set; }
    public string TreasuryAccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? PaymentMethod { get; set; }
    public Guid? CheckId { get; set; }
    public string? CheckNumber { get; set; }
    public Guid? SupplierInvoiceId { get; set; }
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Pays a supplier. Posting writes an OUT TreasuryTransaction (the real bank/cash
/// movement) and a Credit supplier-ledger row (the payable reduction). Paying with
/// an endorsed customer check links the existing check instead of creating cash
/// (spec section 39) - the check itself is moved by the checks module.
/// </summary>
public record PaySupplierCommand(
    DateTime PaymentDate,
    Guid SupplierId,
    Guid TreasuryAccountId,
    decimal Amount,
    string? PaymentMethod = null,
    Guid? CheckId = null,
    Guid? SupplierInvoiceId = null,
    string? Description = null) : IRequest<SupplierPaymentDto>;

public class PaySupplierCommandValidator : AbstractValidator<PaySupplierCommand>
{
    public PaySupplierCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.TreasuryAccountId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public class PaySupplierCommandHandler : IRequestHandler<PaySupplierCommand, SupplierPaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;

    public PaySupplierCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _periodClose = periodClose; }

    public async Task<SupplierPaymentDto> Handle(PaySupplierCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(request.PaymentDate, cancellationToken);
        await PurchaseRules.EnsureSupplierExists(_db, request.SupplierId, cancellationToken);
        await PurchaseRules.EnsureTreasuryAccountExists(_db, request.TreasuryAccountId, cancellationToken);

        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstAsync(s => s.Id == request.SupplierId, cancellationToken);
        var account = await _db.TreasuryAccounts.AsNoTracking()
            .FirstAsync(a => a.Id == request.TreasuryAccountId, cancellationToken);

        string? checkNumber = null;
        if (request.CheckId.HasValue)
        {
            var check = await _db.Checks.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CheckId.Value, cancellationToken)
                ?? throw new NotFoundException("Check", request.CheckId.Value);
            checkNumber = check.CheckNumber;
        }

        if (request.SupplierInvoiceId.HasValue &&
            !await _db.SupplierInvoices.AnyAsync(i => i.Id == request.SupplierInvoiceId.Value, cancellationToken))
            throw new NotFoundException("SupplierInvoice", request.SupplierInvoiceId.Value);

        var paymentNumber = await _numberGenerator.NextAsync(DocumentType.SupplierPayment, cancellationToken: cancellationToken);

        var payment = new SupplierPayment(paymentNumber, request.PaymentDate, request.SupplierId,
            request.TreasuryAccountId, request.Amount, _currentUser.UserName, request.PaymentMethod,
            request.CheckId, checkNumber, request.SupplierInvoiceId, request.Description);

        _db.SupplierPayments.Add(payment);

        // Real cash/bank movement.
        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.TreasuryAccountId, request.PaymentDate, DocumentType.SupplierPayment, paymentNumber, payment.Id,
            request.Amount, TreasuryDirection.Out,
            request.Description ?? $"Payment to supplier {supplier.Name}", _currentUser.UserName));

        // Payable reduction on the append-only supplier ledger.
        _db.SupplierLedgerEntries.Add(new SupplierLedgerEntry(
            request.SupplierId, request.PaymentDate, DocumentType.SupplierPayment, paymentNumber, payment.Id,
            debit: 0m, credit: request.Amount,
            description: request.Description ?? $"Payment to supplier {supplier.Name}", createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierPaymentDto
        {
            Id = payment.Id,
            PaymentNumber = payment.PaymentNumber,
            PaymentDate = payment.PaymentDate,
            SupplierId = payment.SupplierId,
            SupplierCode = supplier.Code,
            SupplierName = supplier.Name,
            TreasuryAccountId = payment.TreasuryAccountId,
            TreasuryAccountName = account.Name,
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

/// <summary>Supplier payments list (spec sections 41 and 48).</summary>
public record GetSupplierPaymentsQuery(Guid? SupplierId = null, DateTime? From = null, DateTime? To = null)
    : IRequest<List<SupplierPaymentDto>>;

public class GetSupplierPaymentsQueryHandler : IRequestHandler<GetSupplierPaymentsQuery, List<SupplierPaymentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetSupplierPaymentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<SupplierPaymentDto>> Handle(GetSupplierPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.SupplierPayments.AsNoTracking().AsQueryable();

        if (request.SupplierId.HasValue) query = query.Where(p => p.SupplierId == request.SupplierId);
        if (request.From.HasValue) query = query.Where(p => p.PaymentDate >= request.From);
        if (request.To.HasValue) query = query.Where(p => p.PaymentDate <= request.To);

        var payments = await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (payments.Count == 0) return new List<SupplierPaymentDto>();

        var supplierIds = payments.Select(p => p.SupplierId).Distinct().ToList();
        var accountIds = payments.Select(p => p.TreasuryAccountId).Distinct().ToList();

        var suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Code, s.Name })
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), cancellationToken);

        var accounts = await _db.TreasuryAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return payments.Select(p =>
        {
            var supplier = suppliers.TryGetValue(p.SupplierId, out var s) ? s : (Code: "", Name: "");
            return new SupplierPaymentDto
            {
                Id = p.Id,
                PaymentNumber = p.PaymentNumber,
                PaymentDate = p.PaymentDate,
                SupplierId = p.SupplierId,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                TreasuryAccountId = p.TreasuryAccountId,
                TreasuryAccountName = accounts.GetValueOrDefault(p.TreasuryAccountId) ?? string.Empty,
                Amount = p.Amount,
                Currency = p.Currency,
                PaymentMethod = p.PaymentMethod,
                CheckId = p.CheckId,
                CheckNumber = p.CheckNumber,
                SupplierInvoiceId = p.SupplierInvoiceId,
                Description = p.Description,
                CreatedBy = p.CreatedBy,
                CreatedAtUtc = p.CreatedAtUtc
            };
        }).ToList();
    }
}
