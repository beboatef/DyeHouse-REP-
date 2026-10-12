using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

/// <summary>
/// Period-closure guard (spec section 43), safe against the close-vs-post race.
///
/// PROTECTED-POSTING PROTOCOL (S/X on one global database app lock):
///   * <see cref="BeginPostingAsync"/> takes a SHARED application lock (sp_getapplock,
///     session-scoped) on the resource <see cref="PeriodLockResource"/>,
///     then re-checks that the posting date is not inside a closed period WHILE
///     HOLDING the lock. The returned guard must be disposed AFTER the handler's
///     SaveChangesAsync; held-through-commit is what closes the race.
///   * ClosePeriodCommandHandler / ReopenPeriodCommandHandler take the EXCLUSIVE
///     version of the same resource through IAllocationLockService.AcquireExclusiveAsync,
///     so a closure can only commit when no posting holds a Shared lock - and a
///     posting can only pass its re-check when the Exclusive side has not committed a
///     close covering its date.
///   * <see cref="EnsureOpenAsync"/> remains the plain (unsynchronized) check; the
///     posting handlers no longer use it, and nothing in Application persists through
///     it. Kept so read-only, non-persisting call sites keep a single simple method.
///
/// The resource is deliberately a single GLOBAL string (never per month or per date):
/// PeriodClose rows are arbitrary, possibly overlapping ranges, so per-range keys
/// could not serialize an Exclusive close against every affected posting.
///
/// The lock lives on the scoped DbContext's own connection; every subsequent query
/// and the SaveChangesAsync of the posting run on that same open session, so the
/// critical section really does span the ledger commit. A 15-second wait fails
/// loudly (StockLockTimeoutException) instead of queueing forever.
/// </summary>
public class PeriodCloseService : IPeriodCloseService
{
    private readonly ApplicationDbContext _context;
    private readonly IAllocationLockService _lockService;

    public PeriodCloseService(ApplicationDbContext context, IAllocationLockService lockService)
    { _context = context; _lockService = lockService; }

    public async Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var closedPeriod = await FirstClosedAsync(date, cancellationToken);
        if (closedPeriod is not null)
            throw ClosedPeriod(closedPeriod);
    }

    public async Task<IAsyncDisposable> BeginPostingAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        // The SHARED period lock comes from the SAME lock service the stock paths
        // use, on the SAME scoped connection, so the holder's SaveChangesAsync is
        // genuinely covered (identical discipline to the stock critical sections).
        var guard = await _lockService.AcquireSharedAsync(IPeriodCloseService.PeriodLockResource, cancellationToken);

        // Re-check closure while HOLDING the lock: if a Close committed between the
        // caller's earlier reads and now, the Exclusive side has finished and this
        // re-check sees its committed row; if a Close is waiting right now, the
        // caller holds Shared and the Close waits its turn - check and commit are
        // one atomic step against the closure.
        try
        {
            var closedPeriod = await FirstClosedAsync(date, cancellationToken);
            if (closedPeriod is not null)
            {
                await guard.DisposeAsync();
                throw ClosedPeriod(closedPeriod);
            }
        }
        catch
        {
            // Never leak the lock above - release whatever was taken before
            // rethrowing the closure refusal (or any unexpected re-check error).
            await guard.DisposeAsync();
            throw;
        }

        return guard;
    }

    private async Task<Domain.Entities.PeriodClose?> FirstClosedAsync(DateTime date, CancellationToken cancellationToken)
        => await _context.PeriodCloses
            .Where(p => !p.IsReopened && date.Date >= p.PeriodStart && date.Date <= p.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);

    private static DomainException ClosedPeriod(Domain.Entities.PeriodClose closedPeriod)
        => new(
            $"The period {closedPeriod.PeriodStart:yyyy-MM-dd} to {closedPeriod.PeriodEnd:yyyy-MM-dd} is closed. " +
            "This date falls inside it - use a reversal/correction document, or an authorized user must reopen the period first.");
}
