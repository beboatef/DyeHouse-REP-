using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
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
        // A new sale may only draw from a warehouse that exists and is still active;
        // deactivated warehouses stay readable for everything already sold from them.
        var warehouse = await WarehouseGuard.EnsureUsableForNewDocumentAsync(_db, request.WarehouseId, cancellationToken);
        if (warehouse.Kind != WarehouseKind.Materials)
            throw new DomainException("Material sales must use a Materials warehouse.");

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

        // A line on a DRAFT is not a movement yet, but posting the sale will draw it out of this
        // warehouse - so a retired warehouse cannot be extended with new lines either. (A sale
        // posted before the warehouse was retired never accepts another line.)
        var saleWarehouse = await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, sale.WarehouseId, cancellationToken, "to add a line to it");
        if (saleWarehouse.Kind != WarehouseKind.Materials)
            throw new DomainException("Material sales must use a Materials warehouse.");

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);

        // Selling is for factory-owned materials/chemicals only. Operating
        // supplies are internal consumables and are issued, never sold here.
        if (material.Kind != MaterialKind.Chemical)
            throw new DomainException("Operating supplies are internal consumables and cannot be sold through the materials sales document.");
        WarehouseGuard.EnsureMaterialWarehouseCompatibility(material.Kind, saleWarehouse);

        var balance = await _ledger.GetBalanceAsync(material.Id, sale.WarehouseId, cancellationToken);
        var requested = sale.Lines.Where(l => l.MaterialId == material.Id).Sum(l => l.Quantity) + request.Quantity;
        if (requested > balance)
            throw new NegativeStockException(balance, requested);

        var line = sale.AddLine(material.Id, request.Quantity, material.Unit, request.UnitPrice ?? material.PurchasePrice, request.Description);

        // A line added to a sale that is ALREADY persisted keeps the key it was created with, so EF
        // change detection would take it for an existing row and save an UPDATE that hits nothing
        // (DbUpdateConcurrencyException) - HTTP 409 and the line is lost. Registering it explicitly makes
        // SaveChangesAsync INSERT it, exactly like a new PurchaseOrderLine or RawAllocation.
        _db.MaterialSaleLines.Add(line);

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
        // One clock read: the guard and EVERY row this post writes (stock, customer, treasury)
        // share the identical posting date, so a midnight cross can never split them.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        // Posting writes one OUT row per line, so the warehouse must still be active: a draft
        // written while it was active is not a licence to draw from it later.
        var saleWarehouse = await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, sale.WarehouseId, cancellationToken, "to post a stock movement out of it");
        if (saleWarehouse.Kind != WarehouseKind.Materials)
            throw new DomainException("Material sales must use a Materials warehouse.");

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
            var material = await _db.Materials.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == line.MaterialId, cancellationToken)
                ?? throw new NotFoundException("Material", line.MaterialId);
            if (material.Kind != MaterialKind.Chemical)
                throw new DomainException("A material sale can contain production chemicals only; operating supplies cannot be sold.");
            WarehouseGuard.EnsureMaterialWarehouseCompatibility(material.Kind, saleWarehouse);
            var balance = await _ledger.GetBalanceAsync(line.MaterialId, sale.WarehouseId, cancellationToken);
            if (line.Quantity > balance) throw new NegativeStockException(balance, line.Quantity);
        }

        sale.Post(_currentUser.UserName);

        foreach (var line in sale.Lines)
        {
            _db.MaterialTransactions.Add(new MaterialTransaction(
                DocumentType.MaterialSale, sale.SaleNumber, sale.Id, postingDate,
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
        // One clock read for the guard and all reversal rows below.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var sale = await _db.MaterialSales.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialSale", request.Id);

        var wasPosted = sale.Status == MaterialSaleStatus.Posted;

        if (wasPosted)
        {
            // Only a POSTED sale is reversed, and the reversal is a brand-new IN row, so the
            // warehouse must still be active for it. Checked BEFORE the document is touched, so
            // a refusal leaves the aggregate, the ledger and the customer account exactly as
            // they were. Cancelling a draft writes no stock row at all, so it needs no check.
            await WarehouseGuard.EnsureUsableForNewMovementAsync(
                _db, sale.WarehouseId, cancellationToken, "to reverse its stock movements");
        }

        sale.Cancel(request.Reason, _currentUser.UserName);

        if (wasPosted)
        {
            foreach (var line in sale.Lines)
            {
                _db.MaterialTransactions.Add(new MaterialTransaction(
                    DocumentType.MaterialSale, sale.SaleNumber, sale.Id, postingDate,
                    line.MaterialId, sale.WarehouseId, null,
                    line.Quantity, MaterialTransactionDirection.In, line.UnitPrice, _currentUser.UserName));
            }

            if (sale.CustomerId is not null)
            {
                _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                    sale.CustomerId.Value, postingDate.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                    0, sale.Total, $"Cancelled material sale {sale.SaleNumber}: {request.Reason}", _currentUser.UserName));

                if (sale.TreasuryAccountId is not null)
                {
                    _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
                        sale.CustomerId.Value, postingDate.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                        sale.Total, 0, $"Cancelled material sale {sale.SaleNumber} - reversing settled cash", _currentUser.UserName));
                }
            }

            if (sale.TreasuryAccountId is not null)
            {
                _db.TreasuryTransactions.Add(new TreasuryTransaction(
                    sale.TreasuryAccountId.Value, postingDate.Date, DocumentType.MaterialSale, sale.SaleNumber, sale.Id,
                    sale.Total, TreasuryDirection.Out,
                    $"Cancelled material sale {sale.SaleNumber}: {request.Reason}", _currentUser.UserName));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (await MaterialSaleDtoBuilder.BuildManyAsync(_db, new[] { sale }, cancellationToken))[0];
    }
}
