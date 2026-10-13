using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Materials.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
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

    public CancelMaterialIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; }

    public async Task<MaterialIssueDto> Handle(CancelMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal row is dated today, so only a period that covers today
        // blocks it - a historical issue can still be reversed. One clock read for the guard and
        // for the compensating ledger row below.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var issue = await _db.MaterialIssues.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("MaterialIssue", request.Id);

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
