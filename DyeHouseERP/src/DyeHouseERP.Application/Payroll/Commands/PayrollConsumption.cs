using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>
/// The two things a payroll run CONSUMES when it is posted, and gives back when it
/// is cancelled or reversed:
///
///   * the approved overtime entries of its period are marked as paid by this run,
///   * the scheduled advance instalments on its lines are actually recovered.
///
/// Keeping consume and release in one place is what makes cancellation and
/// reversal CORRECT rather than merely balanced: the treasury rows reverse the
/// money either way, but without the release the overtime hours would stay
/// attached to a dead run (never payable again) and the reversal's adjustment run
/// would find its advances already recovered and its overtime already consumed,
/// silently under-paying the correction.
/// </summary>
internal static class PayrollConsumption
{
    public static async Task ConsumeAsync(
        IApplicationDbContext db, PayrollRun run, string userName, CancellationToken cancellationToken)
    {
        var employeeIds = run.Lines.Select(l => l.EmployeeId).Distinct().ToList();
        if (employeeIds.Count == 0) return;

        var (periodFrom, periodTo) = Period(run);

        // Advance instalments: the line already carries the scheduled amount, so
        // posting is where that intent becomes a real recovery.
        var advances = await RecoverableAdvancesAsync(db, employeeIds, cancellationToken);
        foreach (var line in run.Lines)
        {
            var remaining = line.Advances;
            if (remaining <= 0) continue;

            foreach (var advance in advances.Where(a => a.EmployeeId == line.EmployeeId))
            {
                if (remaining <= 0) break;
                remaining -= advance.Recover(remaining, userName);
            }
        }

        // Only the overtime of THIS run's period is consumed - an approved entry for
        // another month stays available for that month's run.
        var overtime = await db.OvertimeEntries
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.Status == OvertimeStatus.Approved
                        && o.PayrollRunId == null && o.WorkDate >= periodFrom && o.WorkDate <= periodTo)
            .ToListAsync(cancellationToken);
        foreach (var entry in overtime) entry.MarkPaid(run.Id, userName);
    }

    public static async Task ReleaseAsync(
        IApplicationDbContext db, PayrollRun run, string userName, CancellationToken cancellationToken)
    {
        var employeeIds = run.Lines.Select(l => l.EmployeeId).Distinct().ToList();
        if (employeeIds.Count == 0) return;

        var overtime = await db.OvertimeEntries
            .Where(o => o.PayrollRunId == run.Id)
            .ToListAsync(cancellationToken);
        foreach (var entry in overtime) entry.ReleasePayment(run.Id, userName);

        // Give back the advance instalments this run recovered, in the same
        // (deterministic) order the recovery used, so each advance returns to the
        // state it was in before the run was posted.
        var advances = await db.EmployeeAdvances
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .OrderBy(a => a.AdvanceDate).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        foreach (var line in run.Lines)
        {
            var remaining = line.Advances;
            if (remaining <= 0) continue;

            foreach (var advance in advances.Where(a => a.EmployeeId == line.EmployeeId))
            {
                if (remaining <= 0) break;
                var before = advance.RecoveredAmount;
                advance.Release(remaining, userName);
                remaining -= before - advance.RecoveredAmount;
            }
        }
    }

    private static Task<List<EmployeeAdvance>> RecoverableAdvancesAsync(
        IApplicationDbContext db, List<Guid> employeeIds, CancellationToken cancellationToken)
        => db.EmployeeAdvances
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Status == AdvanceStatus.Active)
            .OrderBy(a => a.AdvanceDate).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    private static (DateTime From, DateTime To) Period(PayrollRun run)
    {
        var from = new DateTime(run.PeriodYear, run.PeriodMonth, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }
}
