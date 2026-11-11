using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Kpis.Queries;

/// <summary>
/// Machine utilisation per period (spec Part 1 metric 3): running time from
/// completed production stage executions (StartedAtUtc to CompletedAtUtc,
/// clipped to the period) divided by available shift time (active shifts'
/// HoursPerDay times days in the period). Only executions with both timestamps
/// are counted; in-progress executions have no end yet and are excluded, never
/// estimated. Requires the new ProductionShift.HoursPerDay field - no other
/// source for available time exists, so it was added with a migration instead
/// of guessed.
/// </summary>
public record GetMachineUtilisationKpiQuery(DateTime? From = null, DateTime? To = null)
    : IRequest<MachineUtilisationKpiResult>, IKpiRange;

public class GetMachineUtilisationKpiQueryValidator : KpiRangeValidator<GetMachineUtilisationKpiQuery>;

public class GetMachineUtilisationKpiQueryHandler : IRequestHandler<GetMachineUtilisationKpiQuery, MachineUtilisationKpiResult>
{
    private readonly IApplicationDbContext _db;
    public GetMachineUtilisationKpiQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<MachineUtilisationKpiResult> Handle(GetMachineUtilisationKpiQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var fromDate = (request.From ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)).Date;
        var toDate = (request.To ?? new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 0, 0, 0, DateTimeKind.Utc)).Date;
        var toExclusive = toDate.AddDays(1);

        var executions = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(e => e.Status == StageExecutionStatus.Completed
                && e.StartedAtUtc != null && e.CompletedAtUtc != null
                && e.StartedAtUtc < toExclusive && e.CompletedAtUtc > fromDate)
            .Select(e => new { e.MachineId, e.ProductionShiftId, Started = e.StartedAtUtc!.Value, Completed = e.CompletedAtUtc!.Value })
            .ToListAsync(cancellationToken);

        var machines = await _db.Machines.AsNoTracking().ToListAsync(cancellationToken);
        var shifts = await _db.ProductionShifts.AsNoTracking().ToListAsync(cancellationToken);

        var daysInPeriod = (toDate - fromDate).Days + 1;
        var plantAvailable = shifts.Where(s => s.IsActive).Sum(s => (double)s.HoursPerDay) * daysInPeriod;

        double OverlapHours(DateTime start, DateTime end)
        {
            var s = start < fromDate ? fromDate : start;
            var e = end > toExclusive ? toExclusive : end;
            return e > s ? (e - s).TotalHours : 0;
        }

        var machineIds = executions.Where(e => e.MachineId.HasValue).Select(e => e.MachineId!.Value)
            .Concat(machines.Where(m => m.IsActive).Select(m => m.Id))
            .Distinct().ToList();
        var machineById = machines.ToDictionary(m => m.Id);

        var result = new MachineUtilisationKpiResult
        {
            From = fromDate,
            To = toDate,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            ComputedAtUtc = DateTime.UtcNow,
            DaysInPeriod = daysInPeriod
        };

        foreach (var id in machineIds)
        {
            machineById.TryGetValue(id, out var machine);
            var running = executions.Where(e => e.MachineId == id).Sum(e => OverlapHours(e.Started, e.Completed));
            result.Machines.Add(new MachineUtilisationRow
            {
                MachineId = id,
                MachineCode = machine?.Code ?? "?",
                MachineName = machine?.Name ?? "?",
                RunningHours = Math.Round(running, 2),
                AvailableHours = Math.Round(plantAvailable, 2),
                UtilisationPct = KpiQueryHelpers.DivideOrNull(running, plantAvailable)
            });
        }
        result.Machines = result.Machines.OrderByDescending(m => m.RunningHours).ToList();

        var shiftIds = executions.Where(e => e.ProductionShiftId.HasValue).Select(e => e.ProductionShiftId!.Value)
            .Concat(shifts.Select(s => s.Id))
            .Distinct().ToList();
        var shiftById = shifts.ToDictionary(s => s.Id);

        foreach (var id in shiftIds)
        {
            shiftById.TryGetValue(id, out var shift);
            var available = shift is { IsActive: true } ? (double)shift.HoursPerDay * daysInPeriod : 0;
            var running = executions.Where(e => e.ProductionShiftId == id).Sum(e => OverlapHours(e.Started, e.Completed));
            result.Shifts.Add(new ShiftUtilisationRow
            {
                ProductionShiftId = id,
                ShiftCode = shift?.Code ?? "?",
                ShiftName = shift?.Name ?? "?",
                HoursPerDay = shift?.HoursPerDay ?? 0m,
                RunningHours = Math.Round(running, 2),
                AvailableHours = Math.Round(available, 2),
                UtilisationPct = KpiQueryHelpers.DivideOrNull(running, available)
            });
        }
        result.Shifts = result.Shifts.OrderBy(s => s.ShiftCode).ToList();

        result.TotalRunningHours = Math.Round(result.Machines.Sum(m => m.RunningHours), 2);
        result.TotalAvailableHours = Math.Round(plantAvailable, 2);
        result.OverallUtilisationPct = KpiQueryHelpers.DivideOrNull(result.TotalRunningHours, result.TotalAvailableHours);
        return result;
    }
}
