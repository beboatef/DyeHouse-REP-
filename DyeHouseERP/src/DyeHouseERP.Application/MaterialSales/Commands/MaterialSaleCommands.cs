using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.MaterialSales.DTOs;
using DyeHouseERP.Application.MaterialSales.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.MaterialSales.Commands;

/// <summary>
/// Creates the DRAFT of a factory-owned materials sale (spec section 26).
/// Nothing moves here: posting is the only step with a stock or financial
/// effect, and it posts the ledger, the receivable and the cash together.
/// </summary>
public record CreateMaterialSaleCommand(
    DateTime SaleDate, Guid WarehouseId, string BuyerName, Guid? CustomerId = null,
    Guid? TreasuryAccountId = null, string? PaymentMethod = null,
    decimal Discount = 0, decimal Tax = 0, string? Notes = null) : IRequest<MaterialSaleDto>;

public class CreateMaterialSaleCommandValidator : AbstractValidator<CreateMaterialSaleCommand>
{
    public CreateMaterialSaleCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.BuyerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tax).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateMaterialSaleCommandHandler : IRequestHandler<CreateMaterialSaleCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateMaterialSaleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; }

    public async Task<MaterialSaleDto> Handle(CreateMaterialSaleCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", request.WarehouseId);

        if (request.CustomerId is not null &&
            !await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
            throw new NotFoundException("Customer", request.CustomerId);

        if (request.TreasuryAccountId is not null &&
            !await _db.TreasuryAccounts.AnyAsync(a => a.Id == request.TreasuryAccountId, cancellationToken))
            throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId);

        var number = await _numberGenerator.NextAsync(DocumentType.MaterialSale, cancellationToken: cancellationToken);

        var sale = new MaterialSale(number, request.SaleDate, warehouse.Id, request.BuyerName, _currentUser.UserName,
            request.CustomerId, request.TreasuryAccountId, request.PaymentMethod,
            request.Discount, request.Tax, request.Notes);

        _db.MaterialSales.Add(sale);
        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

public record AddMaterialSaleLineCommand(Guid MaterialSaleId, Guid MaterialId, decimal Quantity,
    decimal? UnitPrice = null, string? Description = null) : IRequest<MaterialSaleDto>;

public class AddMaterialSaleLineCommandValidator : AbstractValidator<AddMaterialSaleLineCommand>
{
    public AddMaterialSaleLineCommandValidator()
    {
        RuleFor(x => x.MaterialSaleId).NotEmpty();
        RuleFor(x => x.MaterialId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice is not null);
    }
}

public class AddMaterialSaleLineCommandHandler : IRequestHandler<AddMaterialSaleLineCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IMaterialLedgerService _ledger;

    public AddMaterialSaleLineCommandHandler(IApplicationDbContext db, IMaterialLedgerService ledger)
    { _db = db; _ledger = ledger; }

    public async Task<MaterialSaleDto> Handle(AddMaterialSaleLineCommand request, CancellationToken cancellationToken)
    {
        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.MaterialSaleId, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.MaterialSaleId);

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);

        // Selling is for factory-owned materials/chemicals only. Operating
        // supplies are internal consumables and are issued, never sold here.
        if (material.Kind != MaterialKind.Chemical)
            throw new DomainException("Operating supplies are internal consumables and cannot be sold through the materials sales document.");

        var balance = await _ledger.GetBalanceAsync(material.Id, sale.WarehouseId, cancellationToken);
        var requested = sale.Lines.Where(l => l.MaterialId == material.Id).Sum(l => l.Quantity) + request.Quantity;
        if (requested > balance)
            throw new NegativeStockException(balance, requested);

        sale.AddLine(material.Id, request.Quantity, material.Unit, request.UnitPrice ?? material.PurchasePrice, request.Description);
        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

public record RemoveMaterialSaleLineCommand(Guid MaterialSaleId, Guid LineId) : IRequest<MaterialSaleDto>;

public class RemoveMaterialSaleLineCommandHandler : IRequestHandler<RemoveMaterialSaleLineCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    public RemoveMaterialSaleLineCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<MaterialSaleDto> Handle(RemoveMaterialSaleLineCommand request, CancellationToken cancellationToken)
    {
        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.MaterialSaleId, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.MaterialSaleId);

        sale.RemoveLine(request.LineId);
        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

public record SetMaterialSaleTermsCommand(Guid Id, decimal Discount, decimal Tax,
    Guid? TreasuryAccountId, string? PaymentMethod) : IRequest<MaterialSaleDto>;

public class SetMaterialSaleTermsCommandHandler : IRequestHandler<SetMaterialSaleTermsCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public SetMaterialSaleTermsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<MaterialSaleDto> Handle(SetMaterialSaleTermsCommand request, CancellationToken cancellationToken)
    {
        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        sale.SetTerms(request.Discount, request.Tax, request.TreasuryAccountId, request.PaymentMethod, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

/// <summary>
/// Posts the sale (spec section 26). Atomic by construction - one SaveChanges:
/// an OUT MaterialTransaction per line (stock leaves the materials store), a
/// Debit CustomerLedgerEntry when the buyer has a customer account, and an IN
/// TreasuryTransaction when an account is given (paid on delivery). A walk-in
/// buyer with no customer account and no account given simply takes the stock
/// against a named buyer.
/// </summary>
public record PostMaterialSaleCommand(Guid Id) : IRequest<MaterialSaleDto>;

public class PostMaterialSaleCommandHandler : IRequestHandler<PostMaterialSaleCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IMaterialLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public PostMaterialSaleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IMaterialLedgerService ledger, IDateTime clock, IPeriodCloseService periodClose,
        IAllocationLockService stockLock)
    { _db = db; _currentUser = currentUser; _ledger = ledger; _clock = clock; _periodClose = periodClose; _stockLock = stockLock; }

    public async Task<MaterialSaleDto> Handle(PostMaterialSaleCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        // H6: lock every material this sale consumes, then re-check availability
        // and post under those locks.
        var lockKeys = sale.Lines
            .Select(l => StockLockKey.MaterialLot(sale.WarehouseId, l.MaterialId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
        foreach (var line in sale.Lines)
        {
            var balance = await _ledger.GetBalanceAsync(line.MaterialId, sale.WarehouseId, cancellationToken);
            if (line.Quantity > balance) throw new NegativeStockException(balance, line.Quantity);
        }

        sale.Post(_currentUser.UserName);

        foreach (var line in sale.Lines)
        {
            _db.MaterialTransactions.Add(new MaterialTransaction(
                DocumentType.MaterialSale, sale.SaleNumber, sale.Id, _clock.UtcNow,
                line.MaterialId, sale.WarehouseId, null,
                line.Quantity, MaterialTransactionDirection.Out, line.UnitPrice, _currentUser.UserName));
        }

        if (sale.CustomerId is not null)
        {
            _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                sale.CustomerId.Value, sale.SaleDate, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                sale.Total, 0, $"Material sale {sale.SaleNumber}", _currentUser.UserName));
        }

        if (sale.TreasuryAccountId is not null)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                sale.TreasuryAccountId.Value, sale.SaleDate, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                sale.Total, TreasuryDirection.In,
                $"Material sale {sale.SaleNumber} - {sale.BuyerName}", _currentUser.UserName));

            // Paid on the spot: the cash credit settles the receivable just posted.
            if (sale.CustomerId is not null)
            {
                _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                    sale.CustomerId.Value, sale.SaleDate, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                    0, sale.Total, $"Material sale {sale.SaleNumber} settled on delivery", _currentUser.UserName));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        }

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}

/// <summary>Cancels a sale (spec section 10): equal-and-opposite rows, never a delete.</summary>
public record CancelMaterialSaleCommand(Guid Id, string Reason) : IRequest<MaterialSaleDto>;

public class CancelMaterialSaleCommandValidator : AbstractValidator<CancelMaterialSaleCommand>
{
    public CancelMaterialSaleCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class CancelMaterialSaleCommandHandler : IRequestHandler<CancelMaterialSaleCommand, MaterialSaleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public CancelMaterialSaleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; }

    public async Task<MaterialSaleDto> Handle(CancelMaterialSaleCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal rows are dated today, so a closed period only
        // blocks this if today is closed - historical corrections stay possible.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        var wasPosted = sale.Status == MaterialSaleStatus.Posted;

        sale.Cancel(request.Reason, _currentUser.UserName);

        if (wasPosted)
        {
            foreach (var line in sale.Lines)
            {
                _db.MaterialTransactions.Add(new MaterialTransaction(
                    DocumentType.MaterialSale, sale.SaleNumber, sale.Id, _clock.UtcNow,
                    line.MaterialId, sale.WarehouseId, null,
                    line.Quantity, MaterialTransactionDirection.In, line.UnitPrice, _currentUser.UserName));
            }

            if (sale.CustomerId is not null)
            {
                _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                    sale.CustomerId.Value, _clock.UtcNow.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                    0, sale.Total, $"Cancelled material sale {sale.SaleNumber}: {request.Reason}", _currentUser.UserName));

                if (sale.TreasuryAccountId is not null)
                {
                    _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                        sale.CustomerId.Value, _clock.UtcNow.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                        sale.Total, 0, $"Cancelled material sale {sale.SaleNumber} - reversing settled cash", _currentUser.UserName));
                }
            }

            if (sale.TreasuryAccountId is not null)
            {
                _db.TreasuryTransactions.Add(new TreasuryTransaction(
                    sale.TreasuryAccountId.Value, _clock.UtcNow.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                    sale.Total, TreasuryDirection.Out,
                    $"Cancelled material sale {sale.SaleNumber}: {request.Reason}", _currentUser.UserName));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}
