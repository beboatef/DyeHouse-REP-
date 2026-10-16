using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Materials.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Commands;

/// <summary>
/// Cancels a material issue posted against a Production Order (spec sections 18/27/43).
///
/// A material issue is a stock posting with no draft state, so cancellation is a
/// reversal, never a delete: the original OUT ledger row stays for audit and an
/// equal-and-opposite IN row is appended, exactly like cancelling a supply issue or a
/// material sale. The document itself is only flipped to Cancelled - balances are never
/// edited in place - and a second cancellation is refused by the domain rule.
/// </summary>
public record CancelMaterialIssueCommand(Guid Id, string Reason) : IRequest<MaterialIssueDto>;

public class CancelMaterialIssueCommandValidator : AbstractValidator<CancelMaterialIssueCommand>
{
    public CancelMaterialIssueCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CancelMaterialIssueCommandHandler : IRequestHandler<CancelMaterialIssueCommand, MaterialIssueDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CancelMaterialIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock; }

    public async Task<MaterialIssueDto> Handle(CancelMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal row is dated today, so only a period that covers today
        // blocks it - a historical issue can still be reversed. One clock read for the guard and
        // for the compensating ledger row below.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var snapshot = await _db.MaterialIssues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialIssue", request.Id);

        if (snapshot.IsCancelled)
            throw new DomainException("This material issue is already cancelled.");

        // H6: cancelling a material issue appends a compensating IN row on the same
        // (warehouse, material) dimension that CreateMaterialIssueCommandHandler and
        // other material movements lock. Acquiring the same StockLockKey.MaterialLot
        // lock and re-reading the live status inside the lock serializes the reversal
        // with concurrent issues and prevents a duplicate concurrent cancellation from
        // crediting the stock twice.
        await using (await _stockLock.AcquireAsync(
            StockLockKey.MaterialLot(snapshot.WarehouseId, snapshot.MaterialId), cancellationToken))
        {
            var issue = await _db.MaterialIssues.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("MaterialIssue", request.Id);

            var liveStatus = await _db.MaterialIssues.AsNoTracking()
                .Where(i => i.Id == issue.Id)
                .Select(i => (MaterialIssueStatus?)i.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (liveStatus == MaterialIssueStatus.Cancelled)
                throw new DomainException("This material issue is already cancelled.");

            // The reversal is a brand-new IN row, so the warehouse must still be active for it. Checked
            // BEFORE the document is touched, so a refusal leaves the issue and the ledger exactly as
            // they were. (An already-cancelled issue never reaches here: Cancel() refuses below.)
            await WarehouseGuard.EnsureUsableForNewMovementAsync(
                _db, issue.WarehouseId, cancellationToken, "to reverse its stock movements");

            issue.Cancel(request.Reason, _currentUser.UserName);

            _db.MaterialTransactions.Add(new MaterialTransaction(
                DocumentType.MaterialIssue, issue.IssueNumber, issue.Id, postingDate,
                issue.MaterialId, issue.WarehouseId, issue.ProductionOrderId,
                issue.Quantity, MaterialTransactionDirection.In, issue.UnitCost, _currentUser.UserName));

            await _db.SaveChangesAsync(cancellationToken);

            return (await GetMaterialIssuesQueryHandler.BuildManyAsync(_db, new[] { issue }, cancellationToken))[0];
        }
    }
}
