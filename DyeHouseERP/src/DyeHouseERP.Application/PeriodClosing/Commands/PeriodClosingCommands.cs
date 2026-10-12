using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.PeriodClosing.DTOs;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.PeriodClosing.Commands;

public record ClosePeriodCommand(DateTime PeriodStart, DateTime PeriodEnd, string? Notes) : IRequest<PeriodCloseDto>;

public class ClosePeriodCommandValidator : AbstractValidator<ClosePeriodCommand>
{
    public ClosePeriodCommandValidator() => RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart);
}

public class ClosePeriodCommandHandler : IRequestHandler<ClosePeriodCommand, PeriodCloseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAllocationLockService _lockService;
    public ClosePeriodCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IAllocationLockService lockService)
    { _db = db; _currentUser = currentUser; _lockService = lockService; }

    public async Task<PeriodCloseDto> Handle(ClosePeriodCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43, close-vs-post protocol: the closure takes the EXCLUSIVE
        // version of the single GLOBAL period lock and holds it through its own
        // commit. Every posting holds SHARED on the same resource for the whole
        // span between its closure re-check and its SaveChangesAsync, so a close
        // can only commit while NO posting is in flight, and a posting's re-check
        // cannot pass while a close is committing. Arbitrary, overlapping period
        // ranges are why the resource is global rather than per-period.
        await using (await _lockService.AcquireExclusiveAsync(IPeriodCloseService.PeriodLockResource, cancellationToken))
        {
            var period = new PeriodClose(request.PeriodStart, request.PeriodEnd, _currentUser.UserName, request.Notes);
            _db.PeriodCloses.Add(period);
            await _db.SaveChangesAsync(cancellationToken);

            return Map(period);
        }
    }

    internal static PeriodCloseDto Map(PeriodClose p) => new()
    {
        Id = p.Id, PeriodStart = p.PeriodStart, PeriodEnd = p.PeriodEnd, Notes = p.Notes,
        IsReopened = p.IsReopened, ReopenedBy = p.ReopenedBy, ReopenedAtUtc = p.ReopenedAtUtc,
        CreatedBy = p.CreatedBy, CreatedAtUtc = p.CreatedAtUtc
    };
}

public record ReopenPeriodCommand(Guid PeriodCloseId, string Reason) : IRequest<PeriodCloseDto>;

public class ReopenPeriodCommandValidator : AbstractValidator<ReopenPeriodCommand>
{
    public ReopenPeriodCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

/// <summary>Reopening a period is itself an audited action (captured automatically by AuditSaveChangesInterceptor as an Update on PeriodClose) - spec section 43 "Reopening must be audited."</summary>
public class ReopenPeriodCommandHandler : IRequestHandler<ReopenPeriodCommand, PeriodCloseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAllocationLockService _lockService;
    public ReopenPeriodCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IAllocationLockService lockService)
    { _db = db; _currentUser = currentUser; _lockService = lockService; }

    public async Task<PeriodCloseDto> Handle(ReopenPeriodCommand request, CancellationToken cancellationToken)
    {
        // Reopening toggles the very closure that blocks postings, so it shares the
        // Close's EXCLUSIVE period lock: a posting cannot observe half-updated
        // IsReopened state, and a close cannot be re-issued on top of it.
        await using (await _lockService.AcquireExclusiveAsync(IPeriodCloseService.PeriodLockResource, cancellationToken))
        {
            var period = await _db.PeriodCloses.FirstOrDefaultAsync(p => p.Id == request.PeriodCloseId, cancellationToken)
                ?? throw new NotFoundException("PeriodClose", request.PeriodCloseId);

            period.Reopen(request.Reason, _currentUser.UserName);
            await _db.SaveChangesAsync(cancellationToken);

            return ClosePeriodCommandHandler.Map(period);
        }
    }
}
