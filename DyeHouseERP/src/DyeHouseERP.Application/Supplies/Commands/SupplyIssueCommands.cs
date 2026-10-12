using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Supplies.DTOs;
using DyeHouseERP.Application.Supplies.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Supplies.Commands;

/// <summary>
/// Creates a DRAFT operating-supplies issue (spec section 27). Lines are added
/// afterwards so the same command shape works for the single-screen UI and for
/// a future mobile flow. Nothing moves until PostSupplyIssueCommand runs.
/// </summary>
public record CreateSupplyIssueCommand(
    DateTime IssueDate, Guid WarehouseId, string IssuedTo, string Purpose,
    Guid? DepartmentId = null, string? Notes = null) : IRequest<SupplyIssueDto>;

public class CreateSupplyIssueCommandValidator : AbstractValidator<CreateSupplyIssueCommand>
{
    public CreateSupplyIssueCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.IssuedTo).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateSupplyIssueCommandHandler : IRequestHandler<CreateSupplyIssueCommand, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateSupplyIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<SupplyIssueDto> Handle(CreateSupplyIssueCommand request, CancellationToken cancellationToken)
    {
        // A new issue may only target a warehouse that exists and is still active;
        // deactivated warehouses stay readable for everything already issued from them.
        var warehouse = await WarehouseGuard.EnsureUsableForNewDocumentAsync(_db, request.WarehouseId, cancellationToken);

        if (request.DepartmentId is not null &&
            !await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId, cancellationToken))
            throw new NotFoundException("Department", request.DepartmentId);

        var number = await _numberGenerator.NextAsync(DocumentType.SupplyIssue, cancellationToken: cancellationToken);

        var issue = new SupplyIssue(number, request.IssueDate, warehouse.Id, request.IssuedTo, request.Purpose,
            _currentUser.UserName, request.DepartmentId, request.Notes);

        _db.SupplyIssues.Add(issue);
        await _db.SaveChangesAsync(cancellationToken);

        return (await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
    }
}

public record AddSupplyIssueLineCommand(Guid SupplyIssueId, Guid MaterialId, decimal Quantity,
    decimal? UnitCost = null, string? Notes = null) : IRequest<SupplyIssueDto>;

public class AddSupplyIssueLineCommandValidator : AbstractValidator<AddSupplyIssueLineCommand>
{
    public AddSupplyIssueLineCommandValidator()
    {
        RuleFor(x => x.SupplyIssueId).NotEmpty();
        RuleFor(x => x.MaterialId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost is not null);
    }
}

public class AddSupplyIssueLineCommandHandler : IRequestHandler<AddSupplyIssueLineCommand, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IMaterialLedgerService _ledger;

    public AddSupplyIssueLineCommandHandler(IApplicationDbContext db, IMaterialLedgerService ledger)
    { _db = db; _ledger = ledger; }

    public async Task<SupplyIssueDto> Handle(AddSupplyIssueLineCommand request, CancellationToken cancellationToken)
    {
        var issue = await _db.SupplyIssues.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.SupplyIssueId, cancellationToken)
            ?? throw new NotFoundException("SupplyIssue", request.SupplyIssueId);

        // A line added to a DRAFT is not a movement yet, but posting the issue will draw it out of
        // this warehouse - so extending a draft that points at a retired warehouse is refused here,
        // exactly as posting it would be. (A warehouse retired after the issue was posted cannot be
        // reached at all: a posted issue never accepts another line.)
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, issue.WarehouseId, cancellationToken, "to add a line to it");

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);

        // The operating-supplies store only accepts supply-classified materials,
        // so a dye or chemical can never be booked as an internal supply issue.
        if (material.Kind != MaterialKind.OperatingSupply)
            throw new DomainException("That item is a production chemical, not an operating supply. Issue it to a Job Order instead.");

        var balance = await _ledger.GetBalanceAsync(material.Id, issue.WarehouseId, cancellationToken);
        var requested = issue.Lines.Where(l => l.MaterialId == material.Id).Sum(l => l.Quantity) + request.Quantity;
        if (requested > balance)
            throw new NegativeStockException(balance, requested);

        var line = issue.AddLine(material.Id, request.Quantity, material.Unit, request.UnitCost ?? material.PurchasePrice, request.Notes);

        // A line added to an issue that is ALREADY persisted keeps the key it was created with, so EF
        // change detection would take it for an existing row and save an UPDATE that hits nothing
        // (DbUpdateConcurrencyException) - HTTP 409 and the line is lost. Registering it explicitly makes
        // SaveChangesAsync INSERT it, exactly like a new PurchaseOrderLine or RawAllocation.
        _db.SupplyIssueLines.Add(line);

        await _db.SaveChangesAsync(cancellationToken);

        return (await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
    }
}

public record RemoveSupplyIssueLineCommand(Guid SupplyIssueId, Guid LineId) : IRequest<SupplyIssueDto>;

public class RemoveSupplyIssueLineCommandHandler : IRequestHandler<RemoveSupplyIssueLineCommand, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    public RemoveSupplyIssueLineCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<SupplyIssueDto> Handle(RemoveSupplyIssueLineCommand request, CancellationToken cancellationToken)
    {
        var issue = await _db.SupplyIssues.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.SupplyIssueId, cancellationToken)
            ?? throw new NotFoundException("SupplyIssue", request.SupplyIssueId);

        issue.RemoveLine(request.LineId);
        await _db.SaveChangesAsync(cancellationToken);

        return (await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
    }
}

/// <summary>
/// Posts the issue: the only step with a stock effect. One OUT MaterialTransaction
/// per line, always keyed to this document so the ledger stays fully traceable
/// back to who drew the supply and why (spec sections 10 + 27).
/// </summary>
public record PostSupplyIssueCommand(Guid Id) : IRequest<SupplyIssueDto>;

public class PostSupplyIssueCommandHandler : IRequestHandler<PostSupplyIssueCommand, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IMaterialLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public PostSupplyIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IMaterialLedgerService ledger, IDateTime clock, IPeriodCloseService periodClose,
        IAllocationLockService stockLock)
    { _db = db; _currentUser = currentUser; _ledger = ledger; _clock = clock; _periodClose = periodClose; _stockLock = stockLock; }

    public async Task<SupplyIssueDto> Handle(PostSupplyIssueCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: posting a supply issue consumes store stock.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var issue = await _db.SupplyIssues.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplyIssue", request.Id);

        // Posting writes one OUT row per line, so the warehouse must still be active. A draft
        // created while it was active is NOT a licence to draw from it afterwards: retiring a
        // warehouse stops all new movements from it, whichever step tries to write them.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, issue.WarehouseId, cancellationToken, "to post a stock movement out of it");

        // Re-check availability at posting time: stock may have moved since the
        // lines were typed. Negative stock is never allowed implicitly.
        // H6: lock every material this issue consumes before re-checking
        // availability and posting the OUT rows.
        var lockKeys = issue.Lines
            .Select(l => StockLockKey.MaterialLot(issue.WarehouseId, l.MaterialId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
        foreach (var line in issue.Lines)
        {
            var balance = await _ledger.GetBalanceAsync(line.MaterialId, issue.WarehouseId, cancellationToken);
            if (line.Quantity > balance) throw new NegativeStockException(balance, line.Quantity);
        }

        issue.Post(_currentUser.UserName);

        foreach (var line in issue.Lines)
        {
            _db.MaterialTransactions.Add(new MaterialTransaction(
                DocumentType.SupplyIssue, issue.IssueNumber, issue.Id, _clock.UtcNow,
                line.MaterialId, issue.WarehouseId, null,
                line.Quantity, MaterialTransactionDirection.Out, line.UnitCost, _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        }

        return (await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
    }
}

/// <summary>
/// Cancels the issue (spec section 10). A posted issue is never deleted: an
/// equal-and-opposite IN row per line restores the stock and the original
/// OUT rows stay in the ledger for audit.
/// </summary>
public record CancelSupplyIssueCommand(Guid Id, string Reason) : IRequest<SupplyIssueDto>;

public class CancelSupplyIssueCommandValidator : AbstractValidator<CancelSupplyIssueCommand>
{
    public CancelSupplyIssueCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class CancelSupplyIssueCommandHandler : IRequestHandler<CancelSupplyIssueCommand, SupplyIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public CancelSupplyIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; }

    public async Task<SupplyIssueDto> Handle(CancelSupplyIssueCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal row is dated today, so only a period that
        // covers today blocks it - historical corrections remain possible.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var issue = await _db.SupplyIssues.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("SupplyIssue", request.Id);

        var wasPosted = issue.Status == SupplyIssueStatus.Posted;

        if (wasPosted)
        {
            // Only a POSTED issue is reversed, and the reversal is a brand-new IN row, so the
            // warehouse must still be active for it. Checked BEFORE the document is touched, so
            // a refusal leaves the aggregate and the ledger exactly as they were. Cancelling a
            // draft writes no row at all, which is why that case needs no check.
            await WarehouseGuard.EnsureUsableForNewMovementAsync(
                _db, issue.WarehouseId, cancellationToken, "to reverse its stock movements");
        }

        issue.Cancel(request.Reason, _currentUser.UserName);

        if (wasPosted)
        {
            foreach (var line in issue.Lines)
            {
                _db.MaterialTransactions.Add(new MaterialTransaction(
                    DocumentType.SupplyIssue, issue.IssueNumber, issue.Id, _clock.UtcNow,
                    line.MaterialId, issue.WarehouseId, null,
                    line.Quantity, MaterialTransactionDirection.In, line.UnitCost, _currentUser.UserName));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (await SupplyIssueDtoBuilder.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
    }
}
