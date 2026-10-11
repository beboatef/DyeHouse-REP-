using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>
/// Shared lookups for the HR workflow commands: the ONE active attendance policy,
/// an employee's rostered HR shift, and the employee itself. Centralised so every
/// command derives lateness/overtime from the same configuration.
/// </summary>
internal static class HrLookup
{
    public static async Task<AttendanceRule> ActiveRuleAsync(IApplicationDbContext db, CancellationToken ct)
        => await db.AttendanceRules.FirstOrDefaultAsync(a => a.IsActive, ct)
           ?? throw new DomainException(
               "No attendance policy is active. Configure one first (HR > attendance policy).");

    public static async Task<Employee> EmployeeAsync(IApplicationDbContext db, Guid employeeId, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
           ?? throw new NotFoundException("Employee", employeeId);

    public static async Task<HrShift?> ShiftOfAsync(IApplicationDbContext db, Employee employee, CancellationToken ct)
        => employee.HrShiftId.HasValue
            ? await db.HrShifts.FirstOrDefaultAsync(s => s.Id == employee.HrShiftId.Value, ct)
            : null;

    public static AttendanceEventDto Map(AttendanceEvent e, Employee? employee) => new()
    {
        Id = e.Id, EmployeeId = e.EmployeeId, EmployeeCode = employee?.Code ?? "",
        EmployeeName = employee?.Name ?? "", WorkDate = e.WorkDate, HrShiftId = e.HrShiftId, ShiftName = e.ShiftName,
        CheckInMinutes = e.CheckInMinutes, CheckOutMinutes = e.CheckOutMinutes, WorkedMinutes = e.WorkedMinutes,
        LateMinutes = e.LateMinutes, EarlyLeaveMinutes = e.EarlyLeaveMinutes, OvertimeMinutes = e.OvertimeMinutes,
        Status = e.Status, Source = e.Source, LeaveRequestId = e.LeaveRequestId, AbsenceDays = e.AbsenceDays,
        Notes = e.Notes
    };
}

// ------------------------------------------------------------------ attendance

/// <summary>
/// Records (or corrects) one employee's attendance for one day. Late / early-leave /
/// overtime minutes are computed server-side from the shift and the policy, so a
/// client can never submit a forged figure (spec sections 30-31).
/// </summary>
public record RecordAttendanceCommand(Guid EmployeeId, DateTime WorkDate, int CheckInMinutes, int CheckOutMinutes,
    AttendanceSource Source = AttendanceSource.Manual, string? Notes = null) : IRequest<AttendanceEventDto>;

public class RecordAttendanceCommandValidator : AbstractValidator<RecordAttendanceCommand>
{
    public RecordAttendanceCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.CheckInMinutes).InclusiveBetween(0, 1439);
        RuleFor(x => x.CheckOutMinutes).InclusiveBetween(0, 1439);
    }
}

public class RecordAttendanceCommandHandler : IRequestHandler<RecordAttendanceCommand, AttendanceEventDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;

    public RecordAttendanceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; }

    public async Task<AttendanceEventDto> Handle(RecordAttendanceCommand r, CancellationToken ct)
    {
        // Attendance belongs to a payroll period, so a closed period blocks edits too.
        await _periodClose.EnsureOpenAsync(r.WorkDate.Date, ct);

        var employee = await HrLookup.EmployeeAsync(_db, r.EmployeeId, ct);
        var shift = await HrLookup.ShiftOfAsync(_db, employee, ct);
        var rule = await HrLookup.ActiveRuleAsync(_db, ct);

        var existing = await _db.AttendanceEvents
            .FirstOrDefaultAsync(a => a.EmployeeId == r.EmployeeId && a.WorkDate == r.WorkDate.Date, ct);

        if (existing is null)
        {
            var created = new AttendanceEvent(r.EmployeeId, r.WorkDate, shift, rule,
                r.CheckInMinutes, r.CheckOutMinutes, r.Source, _currentUser.UserName, r.Notes);
            _db.AttendanceEvents.Add(created);
            await _db.SaveChangesAsync(ct);
            return HrLookup.Map(created, employee);
        }

        existing.ReRecord(shift, rule, r.CheckInMinutes, r.CheckOutMinutes, r.Source, _currentUser.UserName, r.Notes);
        await _db.SaveChangesAsync(ct);
        return HrLookup.Map(existing, employee);
    }
}

/// <summary>Records a non-working day (absence, rest day or a public holiday) with a documented reason.</summary>
public record RecordNonWorkingDayCommand(Guid EmployeeId, DateTime WorkDate, AttendanceStatus Status,
    string? Notes = null) : IRequest<AttendanceEventDto>;

public class RecordNonWorkingDayCommandHandler : IRequestHandler<RecordNonWorkingDayCommand, AttendanceEventDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;

    public RecordNonWorkingDayCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; }

    public async Task<AttendanceEventDto> Handle(RecordNonWorkingDayCommand r, CancellationToken ct)
    {
        await _periodClose.EnsureOpenAsync(r.WorkDate.Date, ct);

        if (r.Status is AttendanceStatus.Present or AttendanceStatus.Late)
            throw new DomainException("Use record-attendance to record a day that was worked.");

        var employee = await HrLookup.EmployeeAsync(_db, r.EmployeeId, ct);
        var shift = await HrLookup.ShiftOfAsync(_db, employee, ct);
        var rule = await HrLookup.ActiveRuleAsync(_db, ct);

        var existing = await _db.AttendanceEvents
            .FirstOrDefaultAsync(a => a.EmployeeId == r.EmployeeId && a.WorkDate == r.WorkDate.Date, ct);
        if (existing is not null)
            _db.AttendanceEvents.Remove(existing);

        var day = AttendanceEvent.NonWorking(r.EmployeeId, r.WorkDate, r.Status, shift, rule, _currentUser.UserName,
            notes: r.Notes);
        _db.AttendanceEvents.Add(day);
        await _db.SaveChangesAsync(ct);
        return HrLookup.Map(day, employee);
    }
}

/// <summary>
/// Fills in a whole period (spec section 31): every active employee gets a row for
/// each day that is NOT one of their shift's working days (RestDay) and for each
/// working day that has no recorded attendance yet, an Absent row - so payroll can
/// never silently pay a day nobody attended. Existing rows are always kept.
/// </summary>
public record GenerateAttendanceForPeriodCommand(int PeriodYear, int PeriodMonth, Guid? DepartmentId = null,
    bool IncludeUnrecordedWorkingDaysAsAbsent = true) : IRequest<int>;

public class GenerateAttendanceForPeriodCommandValidator : AbstractValidator<GenerateAttendanceForPeriodCommand>
{
    public GenerateAttendanceForPeriodCommandValidator()
    {
        RuleFor(x => x.PeriodYear).InclusiveBetween(2000, 2200);
        RuleFor(x => x.PeriodMonth).InclusiveBetween(1, 12);
    }
}

public class GenerateAttendanceForPeriodCommandHandler : IRequestHandler<GenerateAttendanceForPeriodCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;

    public GenerateAttendanceForPeriodCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; }

    public async Task<int> Handle(GenerateAttendanceForPeriodCommand r, CancellationToken ct)
    {
        var from = new DateTime(r.PeriodYear, r.PeriodMonth, 1);
        var to = from.AddMonths(1).AddDays(-1);
        await _periodClose.EnsureOpenAsync(to, ct);

        var rule = await HrLookup.ActiveRuleAsync(_db, ct);

        var employeesQuery = _db.Employees.Where(e => e.Status == EmployeeStatus.Active);
        if (r.DepartmentId.HasValue) employeesQuery = employeesQuery.Where(e => e.DepartmentId == r.DepartmentId.Value);
        var employees = await employeesQuery.ToListAsync(ct);

        var shifts = await _db.HrShifts.ToDictionaryAsync(s => s.Id, ct);
        var existing = await _db.AttendanceEvents
            .Where(a => a.WorkDate >= from && a.WorkDate <= to)
            .Select(a => new { a.EmployeeId, a.WorkDate })
            .ToListAsync(ct);
        var haveRow = existing.Select(x => (x.EmployeeId, x.WorkDate)).ToHashSet();

        var created = 0;
        foreach (var employee in employees)
        {
            shifts.TryGetValue(employee.HrShiftId ?? Guid.Empty, out var shift);
            if (shift is null) continue;

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (haveRow.Contains((employee.Id, date))) continue;

                var worksToday = shift.WorksOn(date.DayOfWeek);
                if (!worksToday)
                {
                    _db.AttendanceEvents.Add(AttendanceEvent.NonWorking(
                        employee.Id, date, AttendanceStatus.RestDay, shift, rule, _currentUser.UserName));
                    created++;
                }
                else if (r.IncludeUnrecordedWorkingDaysAsAbsent)
                {
                    _db.AttendanceEvents.Add(AttendanceEvent.NonWorking(
                        employee.Id, date, AttendanceStatus.Absent, shift, rule, _currentUser.UserName,
                        notes: "Generated: no attendance was recorded for this working day."));
                    created++;
                }
            }
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }
}

/// <summary>Removes a manual attendance row (a correction). A generated row is removed the same way.</summary>
public record RemoveAttendanceEventCommand(Guid Id) : IRequest<Unit>;

public class RemoveAttendanceEventCommandHandler : IRequestHandler<RemoveAttendanceEventCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IPeriodCloseService _periodClose;
    public RemoveAttendanceEventCommandHandler(IApplicationDbContext db, IPeriodCloseService periodClose)
    { _db = db; _periodClose = periodClose; }

    public async Task<Unit> Handle(RemoveAttendanceEventCommand r, CancellationToken ct)
    {
        var attendance = await _db.AttendanceEvents.FirstOrDefaultAsync(a => a.Id == r.Id, ct)
            ?? throw new NotFoundException("AttendanceEvent", r.Id);

        await _periodClose.EnsureOpenAsync(attendance.WorkDate, ct);
        _db.AttendanceEvents.Remove(attendance);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------- leave

/// <summary>Raises a leave request. Only an APPROVED request affects attendance (spec section 32).</summary>
public record SubmitLeaveRequestCommand(Guid EmployeeId, Guid LeaveTypeId, DateTime FromDate, DateTime ToDate,
    decimal Days, string Reason) : IRequest<LeaveRequestDto>;

public class SubmitLeaveRequestCommandValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    public SubmitLeaveRequestCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.Days).GreaterThan(0).LessThanOrEqualTo(366);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x).Must(x => x.ToDate.Date >= x.FromDate.Date)
            .WithMessage("The leave end date cannot precede its start date.");
    }
}

public class SubmitLeaveRequestCommandHandler : IRequestHandler<SubmitLeaveRequestCommand, LeaveRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SubmitLeaveRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<LeaveRequestDto> Handle(SubmitLeaveRequestCommand r, CancellationToken ct)
    {
        var employee = await HrLookup.EmployeeAsync(_db, r.EmployeeId, ct);
        var leaveType = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct)
            ?? throw new NotFoundException("LeaveType", r.LeaveTypeId);
        if (!leaveType.IsActive)
            throw new DomainException($"Leave type '{leaveType.Name}' is not active.");

        // No overlapping open/approved leave for the same employee.
        var overlap = await _db.LeaveRequests.AnyAsync(l => l.EmployeeId == r.EmployeeId
            && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
            && l.FromDate <= r.ToDate.Date && l.ToDate >= r.FromDate.Date, ct);
        if (overlap)
            throw new DomainException("This employee already has pending or approved leave overlapping these dates.");

        var request = new LeaveRequest(r.EmployeeId, r.LeaveTypeId, r.FromDate, r.ToDate, r.Days, r.Reason,
            _currentUser.UserName);
        _db.LeaveRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return HrDtoBuilder.MapLeave(request, employee, leaveType.Name, leaveType.IsPaid);
    }
}

/// <summary>
/// Approves a leave request and writes the leave into attendance, so payroll sees
/// it. Separation of duties: the requester can never approve their own request.
/// </summary>
public record ApproveLeaveRequestCommand(Guid Id, string? Notes = null) : IRequest<LeaveRequestDto>;

public class ApproveLeaveRequestCommandHandler : IRequestHandler<ApproveLeaveRequestCommand, LeaveRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public ApproveLeaveRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; }

    public async Task<LeaveRequestDto> Handle(ApproveLeaveRequestCommand r, CancellationToken ct)
    {
        var request = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == r.Id, ct)
            ?? throw new NotFoundException("LeaveRequest", r.Id);

        if (string.Equals(request.CreatedBy, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Separation of duties: the user requesting leave cannot also approve it.");

        await _periodClose.EnsureOpenAsync(request.FromDate, ct);

        var employee = await HrLookup.EmployeeAsync(_db, request.EmployeeId, ct);
        var leaveType = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.LeaveTypeId, ct)
            ?? throw new NotFoundException("LeaveType", request.LeaveTypeId);
        var shift = await HrLookup.ShiftOfAsync(_db, employee, ct);
        var rule = await HrLookup.ActiveRuleAsync(_db, ct);

        request.Approve(_currentUser.UserName, _clock.UtcNow);

        // Write the leave into attendance exactly once, so a later re-open cannot double it.
        if (!request.AttendanceApplied)
        {
            var existing = await _db.AttendanceEvents
                .Where(a => a.EmployeeId == request.EmployeeId && a.WorkDate >= request.FromDate && a.WorkDate <= request.ToDate)
                .ToListAsync(ct);

            for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
            {
                var row = existing.FirstOrDefault(x => x.WorkDate == date);
                if (row is not null) row.MarkOnLeave(request.Id, _currentUser.UserName);
                else
                    _db.AttendanceEvents.Add(AttendanceEvent.NonWorking(
                        request.EmployeeId, date, AttendanceStatus.OnLeave, shift, rule, _currentUser.UserName,
                        leaveRequestId: request.Id));
            }

            request.MarkAttendanceApplied(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrDtoBuilder.MapLeave(request, employee, leaveType.Name, leaveType.IsPaid);
    }
}

public record RejectLeaveRequestCommand(Guid Id, string Reason) : IRequest<LeaveRequestDto>;

public class RejectLeaveRequestCommandValidator : AbstractValidator<RejectLeaveRequestCommand>
{
    public RejectLeaveRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class RejectLeaveRequestCommandHandler : IRequestHandler<RejectLeaveRequestCommand, LeaveRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public RejectLeaveRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    { _db = db; _currentUser = currentUser; _clock = clock; }

    public async Task<LeaveRequestDto> Handle(RejectLeaveRequestCommand r, CancellationToken ct)
    {
        var request = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == r.Id, ct)
            ?? throw new NotFoundException("LeaveRequest", r.Id);

        if (string.Equals(request.CreatedBy, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Separation of duties: the user requesting leave cannot also reject it.");

        request.Reject(_currentUser.UserName, r.Reason, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        var leaveType = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.LeaveTypeId, ct);
        return HrDtoBuilder.MapLeave(request, employee, leaveType?.Name ?? "", leaveType?.IsPaid ?? false);
    }
}

public record CancelLeaveRequestCommand(Guid Id, string? Reason = null) : IRequest<LeaveRequestDto>;

public class CancelLeaveRequestCommandHandler : IRequestHandler<CancelLeaveRequestCommand, LeaveRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CancelLeaveRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<LeaveRequestDto> Handle(CancelLeaveRequestCommand r, CancellationToken ct)
    {
        var request = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == r.Id, ct)
            ?? throw new NotFoundException("LeaveRequest", r.Id);

        // Authorization: the requester may withdraw their own leave, and someone who
        // can decide leave may cancel it - but a user holding only the plain
        // "request leave" right cannot cancel somebody else's approved leave.
        var isOwner = string.Equals(request.CreatedBy, _currentUser.UserName, StringComparison.OrdinalIgnoreCase);
        if (!isOwner && !_currentUser.HasPermission(Domain.Common.Permissions.HrLeaveApprove))
            throw new UnauthorizedAccessException(
                "Only the user who raised this leave request, or a user allowed to decide leave, can cancel it.");

        var wasApplied = request.AttendanceApplied;
        request.Cancel(_currentUser.UserName, r.Reason);

        // Undo the attendance the approval wrote, so a cancelled leave stops affecting
        // payroll. A day the employee actually WORKED before the leave was approved is
        // restored to its recorded check-in/out (the leave only zeroed it); a day that
        // exists solely because of the leave is removed.
        if (wasApplied)
        {
            var rows = await _db.AttendanceEvents
                .Where(a => a.LeaveRequestId == request.Id)
                .ToListAsync(ct);

            var worked = rows.Where(a => a.CheckInMinutes.HasValue && a.CheckOutMinutes.HasValue).ToList();
            var purelyLeave = rows.Where(a => !a.CheckInMinutes.HasValue || !a.CheckOutMinutes.HasValue).ToList();

            _db.AttendanceEvents.RemoveRange(purelyLeave);

            if (worked.Count > 0)
            {
                var employeeForShift = await HrLookup.EmployeeAsync(_db, request.EmployeeId, ct);
                var shift = await HrLookup.ShiftOfAsync(_db, employeeForShift, ct);
                var rule = await HrLookup.ActiveRuleAsync(_db, ct);

                foreach (var row in worked)
                    row.ReRecord(shift, rule, row.CheckInMinutes!.Value, row.CheckOutMinutes!.Value,
                        row.Source, _currentUser.UserName, row.Notes);
            }
        }

        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        var leaveType = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.LeaveTypeId, ct);
        return HrDtoBuilder.MapLeave(request, employee, leaveType?.Name ?? "", leaveType?.IsPaid ?? false);
    }
}

// ------------------------------------------------------------------- overtime

/// <summary>Requests overtime (spec section 33). It is only paid once approved.</summary>
public record SubmitOvertimeCommand(Guid EmployeeId, DateTime WorkDate, decimal Hours,
    OvertimeCategory Category, string Reason) : IRequest<OvertimeEntryDto>;

public class SubmitOvertimeCommandValidator : AbstractValidator<SubmitOvertimeCommand>
{
    public SubmitOvertimeCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Hours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class SubmitOvertimeCommandHandler : IRequestHandler<SubmitOvertimeCommand, OvertimeEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;

    public SubmitOvertimeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; }

    public async Task<OvertimeEntryDto> Handle(SubmitOvertimeCommand r, CancellationToken ct)
    {
        await _periodClose.EnsureOpenAsync(r.WorkDate.Date, ct);
        var employee = await HrLookup.EmployeeAsync(_db, r.EmployeeId, ct);

        var entry = new OvertimeEntry(r.EmployeeId, r.WorkDate, r.Hours, r.Category, r.Reason, _currentUser.UserName);
        _db.OvertimeEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return HrDtoBuilder.MapOvertime(entry, employee);
    }
}

/// <summary>
/// Approves an overtime entry and SNAPSHOTS the policy multiplier at that moment, so
/// a later policy change never rewrites a paid figure. The requester cannot approve
/// their own overtime.
/// </summary>
public record ApproveOvertimeCommand(Guid Id) : IRequest<OvertimeEntryDto>;

public class ApproveOvertimeCommandHandler : IRequestHandler<ApproveOvertimeCommand, OvertimeEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public ApproveOvertimeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    { _db = db; _currentUser = currentUser; _clock = clock; }

    public async Task<OvertimeEntryDto> Handle(ApproveOvertimeCommand r, CancellationToken ct)
    {
        var entry = await _db.OvertimeEntries.FirstOrDefaultAsync(o => o.Id == r.Id, ct)
            ?? throw new NotFoundException("OvertimeEntry", r.Id);

        if (string.Equals(entry.CreatedBy, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Separation of duties: the user requesting overtime cannot also approve it.");

        var rule = await HrLookup.ActiveRuleAsync(_db, ct);
        entry.Approve(_currentUser.UserName, rule.OvertimeMultiplier(entry.Category), _clock.UtcNow);
        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entry.EmployeeId, ct);
        return HrDtoBuilder.MapOvertime(entry, employee);
    }
}

public record RejectOvertimeCommand(Guid Id, string Reason) : IRequest<OvertimeEntryDto>;

public class RejectOvertimeCommandValidator : AbstractValidator<RejectOvertimeCommand>
{
    public RejectOvertimeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class RejectOvertimeCommandHandler : IRequestHandler<RejectOvertimeCommand, OvertimeEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public RejectOvertimeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    { _db = db; _currentUser = currentUser; _clock = clock; }

    public async Task<OvertimeEntryDto> Handle(RejectOvertimeCommand r, CancellationToken ct)
    {
        var entry = await _db.OvertimeEntries.FirstOrDefaultAsync(o => o.Id == r.Id, ct)
            ?? throw new NotFoundException("OvertimeEntry", r.Id);

        if (string.Equals(entry.CreatedBy, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Separation of duties: the user requesting overtime cannot also reject it.");

        entry.Reject(_currentUser.UserName, r.Reason, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entry.EmployeeId, ct);
        return HrDtoBuilder.MapOvertime(entry, employee);
    }
}

// ------------------------------------------------------------------ advances

/// <summary>Creates an employee advance recovered by payroll in instalments (spec section 34).</summary>
public record CreateEmployeeAdvanceCommand(Guid EmployeeId, DateTime AdvanceDate, decimal Amount,
    int InstalmentCount, string Reason) : IRequest<EmployeeAdvanceDto>;

public class CreateEmployeeAdvanceCommandValidator : AbstractValidator<CreateEmployeeAdvanceCommand>
{
    public CreateEmployeeAdvanceCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.InstalmentCount).GreaterThan(0).LessThanOrEqualTo(120);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CreateEmployeeAdvanceCommandHandler : IRequestHandler<CreateEmployeeAdvanceCommand, EmployeeAdvanceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateEmployeeAdvanceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<EmployeeAdvanceDto> Handle(CreateEmployeeAdvanceCommand r, CancellationToken ct)
    {
        var employee = await HrLookup.EmployeeAsync(_db, r.EmployeeId, ct);

        var advance = new EmployeeAdvance(r.EmployeeId, r.AdvanceDate, r.Amount, r.InstalmentCount, r.Reason,
            _currentUser.UserName);
        _db.EmployeeAdvances.Add(advance);
        await _db.SaveChangesAsync(ct);
        return HrDtoBuilder.MapAdvance(advance, employee);
    }
}

public record CancelEmployeeAdvanceCommand(Guid Id, string Reason) : IRequest<EmployeeAdvanceDto>;

public class CancelEmployeeAdvanceCommandValidator : AbstractValidator<CancelEmployeeAdvanceCommand>
{
    public CancelEmployeeAdvanceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CancelEmployeeAdvanceCommandHandler : IRequestHandler<CancelEmployeeAdvanceCommand, EmployeeAdvanceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CancelEmployeeAdvanceCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<EmployeeAdvanceDto> Handle(CancelEmployeeAdvanceCommand r, CancellationToken ct)
    {
        var advance = await _db.EmployeeAdvances.FirstOrDefaultAsync(a => a.Id == r.Id, ct)
            ?? throw new NotFoundException("EmployeeAdvance", r.Id);

        advance.Cancel(_currentUser.UserName, r.Reason);
        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == advance.EmployeeId, ct);
        return HrDtoBuilder.MapAdvance(advance, employee);
    }
}
