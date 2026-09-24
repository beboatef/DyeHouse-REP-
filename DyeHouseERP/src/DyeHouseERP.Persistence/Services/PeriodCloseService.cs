using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

public class PeriodCloseService : IPeriodCloseService
{
    private readonly ApplicationDbContext _context;
    public PeriodCloseService(ApplicationDbContext context) => _context = context;

    public async Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var closedPeriod = await _context.PeriodCloses
            .Where(p => !p.IsReopened && date.Date >= p.PeriodStart && date.Date <= p.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);

        if (closedPeriod is not null)
            throw new DomainException(
                $"The period {closedPeriod.PeriodStart:yyyy-MM-dd} to {closedPeriod.PeriodEnd:yyyy-MM-dd} is closed. This date falls inside it - use a reversal/correction document, or an authorized user must reopen the period first.");
    }
}
