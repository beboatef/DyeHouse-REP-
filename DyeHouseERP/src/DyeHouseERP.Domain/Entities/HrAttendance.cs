using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// A sub-unit of a department (spec sections 28-29): Dyeing may have a
/// "Preparation" section and a "Dyeing machines" section. Configurable data -
/// the factory defines its own structure; nothing is hard-coded.
/// </summary>
public class HrSection : AuditableEntity
{
    public Guid DepartmentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private HrSection() { } // EF Core

    public HrSection(Guid departmentId, string code, string createdBy,
        string? nameAr = null, string? nameEn = null, string? notes = null)
    {
        if (departmentId == Guid.Empty)
            throw new DomainException("A section must belong to a department.");
        DepartmentId = departmentId;
        SetCode(code);
        SetNames(nameAr, nameEn);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A section requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A section requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void SetDepartment(Guid departmentId)
    {
        if (departmentId == Guid.Empty)
            throw new DomainException("A section must belong to a department.");
        DepartmentId = departmentId;
    }

    public void SetNotes(string? notes) => Notes = Blank(notes);
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// A job position master (spec section 28): dyeing operator, packer, accountant.
/// Carries an optional default basic salary so creating an employee starts from a
/// configured figure instead of a typed one.
/// </summary>
public class JobPosition : AuditableEntity
{
    public Guid? DepartmentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal? DefaultBasicSalary { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private JobPosition() { } // EF Core

    public JobPosition(string code, string createdBy, string? nameAr = null, string? nameEn = null,
        Guid? departmentId = null, decimal? defaultBasicSalary = null, string? notes = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        DepartmentId = departmentId;
        SetDefaultSalary(defaultBasicSalary);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A job requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A job requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void SetDepartment(Guid? departmentId) => DepartmentId = departmentId;

    public void SetDefaultSalary(decimal? salary)
    {
        if (salary.HasValue && salary.Value < 0)
            throw new DomainException("A default basic salary cannot be negative.");
        DefaultBasicSalary = salary;
    }

    public void SetNotes(string? notes) => Notes = Blank(notes);
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// A cost center (spec section 28): where labor cost is collected for reporting.
/// Payroll is still independent of Job Order costing; cost centers only classify
/// the wage bill for labor-cost reporting.
/// </summary>
public class CostCenter : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private CostCenter() { } // EF Core

    public CostCenter(string code, string createdBy, string? nameAr = null, string? nameEn = null, string? notes = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A cost center requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A cost center requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void SetNotes(string? notes) => Notes = Blank(notes);
    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// An HR attendance shift (spec section 30): the shift an EMPLOYEE is rostered on,
/// with start/end time, break, grace and working days.
///
/// This is deliberately NOT <see cref="ProductionShift"/>. That master records
/// which shift a stage execution ran on and is never connected to attendance or
/// payroll; this one drives attendance and, through it, the wage calculation.
/// The two are free to be configured differently, which is exactly the point.
/// </summary>
public class HrShift : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Shift start as minutes from midnight (0-1439) - a day-crossing shift is expressed with a later end.</summary>
    public int StartMinutes { get; private set; }
    public int EndMinutes { get; private set; }

    /// <summary>Unpaid break minutes deducted from the worked time.</summary>
    public int BreakMinutes { get; private set; }

    /// <summary>Minutes after the start that do not count as lateness.</summary>
    public int GraceMinutes { get; private set; }

    /// <summary>Working weekdays as a CSV of DayOfWeek numbers (0=Sunday .. 6=Saturday). Configurable, not fixed.</summary>
    public string WorkingDays { get; private set; } = "0,1,2,3,4";

    public bool IsNightShift { get; private set; }
    public bool IsActive { get; private set; } = true;

    private HrShift() { } // EF Core

    public HrShift(string code, int startMinutes, int endMinutes, string createdBy,
        string? nameAr = null, string? nameEn = null, int breakMinutes = 0, int graceMinutes = 0,
        string? workingDays = null)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        SetTiming(startMinutes, endMinutes, breakMinutes, graceMinutes);
        SetWorkingDays(workingDays);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A shift requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A shift requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    /// <summary>Timing is in minutes from midnight; an end at or before the start means the shift crosses midnight.</summary>
    public void SetTiming(int startMinutes, int endMinutes, int breakMinutes, int graceMinutes)
    {
        if (startMinutes is < 0 or > 1439)
            throw new DomainException("Shift start must be a valid time of day.");
        if (endMinutes is < 0 or > 1439)
            throw new DomainException("Shift end must be a valid time of day.");
        if (startMinutes == endMinutes)
            throw new DomainException("A shift cannot start and end at the same minute.");
        if (breakMinutes < 0 || graceMinutes < 0)
            throw new DomainException("Break and grace minutes cannot be negative.");

        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
        BreakMinutes = breakMinutes;
        GraceMinutes = graceMinutes;
        IsNightShift = endMinutes < startMinutes;
    }

    public void SetWorkingDays(string? workingDays)
    {
        if (string.IsNullOrWhiteSpace(workingDays))
        {
            WorkingDays = "0,1,2,3,4";
            return;
        }

        var days = workingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.TryParse(d, out var v) ? v : -1)
            .Where(v => v is >= 0 and <= 6)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        if (days.Count == 0)
            throw new DomainException("A shift must have at least one working day.");

        WorkingDays = string.Join(',', days);
    }

    public bool WorksOn(DayOfWeek day) => WorkingDays.Split(',').Contains(((int)day).ToString());

    /// <summary>Planned paid minutes for one working day: shift length minus the unpaid break.</summary>
    public int PlannedMinutes
    {
        get
        {
            var length = IsNightShift ? (1440 - StartMinutes) + EndMinutes : EndMinutes - StartMinutes;
            return Math.Max(0, length - BreakMinutes);
        }
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// The configurable attendance policy (spec sections 30-33): standard hours, the
/// grace period, how lateness/absence are penalised and what overtime multipliers
/// apply. One active policy at a time - every rule is DATA, never a constant in
/// the calculation code, because factories differ.
/// </summary>
public class AttendanceRule : AuditableEntity
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>Standard paid hours for one full working day (e.g. 8).</summary>
    public decimal StandardDailyHours { get; private set; } = 8m;

    /// <summary>Standard working days per month used to derive a daily/hourly rate.</summary>
    public decimal StandardWorkingDaysPerMonth { get; private set; } = 26m;

    /// <summary>Minutes after a shift's start that are not counted as lateness.</summary>
    public int GraceMinutes { get; private set; }

    /// <summary>Money deducted per late minute (currency). 0 disables the penalty.</summary>
    public decimal LatePenaltyPerMinute { get; private set; }

    /// <summary>Money deducted per early-leave minute (currency). 0 disables the penalty.</summary>
    public decimal EarlyLeavePenaltyPerMinute { get; private set; }

    /// <summary>How many days' basic pay are deducted for one unexcused absence (1 = one day).</summary>
    public decimal AbsenceDayDeductionFactor { get; private set; } = 1m;

    /// <summary>Minimum overtime minutes after the shift end before any overtime is counted.</summary>
    public int OvertimeGraceMinutes { get; private set; }

    public decimal OvertimeNormalMultiplier { get; private set; } = 1.5m;
    public decimal OvertimeWeekendMultiplier { get; private set; } = 2m;
    public decimal OvertimeHolidayMultiplier { get; private set; } = 2.5m;

    /// <summary>Working weekdays as a CSV of DayOfWeek numbers, used to classify weekend overtime.</summary>
    public string WeekendDays { get; private set; } = "5,6";

    /// <summary>Whether an approved PAID leave still counts as an attendance day for attendance-day components.</summary>
    public bool PaidLeaveCountsAsAttendance { get; private set; } = true;

    /// <summary>Whether overtime must be approved before it is paid (spec section 33).</summary>
    public bool OvertimeRequiresApproval { get; private set; } = true;

    public bool IsActive { get; private set; } = true;

    private AttendanceRule() { } // EF Core

    public AttendanceRule(string name, string createdBy)
    {
        SetName(name);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("An attendance policy requires a name.");
        Name = name.Trim();
    }

    public void Configure(
        decimal standardDailyHours, decimal standardWorkingDaysPerMonth, int graceMinutes,
        decimal latePenaltyPerMinute, decimal earlyLeavePenaltyPerMinute, decimal absenceDayDeductionFactor,
        int overtimeGraceMinutes, decimal overtimeNormalMultiplier, decimal overtimeWeekendMultiplier,
        decimal overtimeHolidayMultiplier, string? weekendDays, bool paidLeaveCountsAsAttendance,
        bool overtimeRequiresApproval)
    {
        if (standardDailyHours <= 0 || standardDailyHours > 24)
            throw new DomainException("Standard daily hours must be between 0 and 24.");
        if (standardWorkingDaysPerMonth <= 0 || standardWorkingDaysPerMonth > 31)
            throw new DomainException("Standard working days per month must be between 1 and 31.");
        if (graceMinutes < 0 || overtimeGraceMinutes < 0)
            throw new DomainException("Grace minutes cannot be negative.");
        if (latePenaltyPerMinute < 0 || earlyLeavePenaltyPerMinute < 0 || absenceDayDeductionFactor < 0)
            throw new DomainException("Penalty amounts cannot be negative.");
        if (overtimeNormalMultiplier <= 0 || overtimeWeekendMultiplier <= 0 || overtimeHolidayMultiplier <= 0)
            throw new DomainException("Overtime multipliers must be positive.");

        StandardDailyHours = standardDailyHours;
        StandardWorkingDaysPerMonth = standardWorkingDaysPerMonth;
        GraceMinutes = graceMinutes;
        LatePenaltyPerMinute = latePenaltyPerMinute;
        EarlyLeavePenaltyPerMinute = earlyLeavePenaltyPerMinute;
        AbsenceDayDeductionFactor = absenceDayDeductionFactor;
        OvertimeGraceMinutes = overtimeGraceMinutes;
        OvertimeNormalMultiplier = overtimeNormalMultiplier;
        OvertimeWeekendMultiplier = overtimeWeekendMultiplier;
        OvertimeHolidayMultiplier = overtimeHolidayMultiplier;
        SetWeekendDays(weekendDays);
        PaidLeaveCountsAsAttendance = paidLeaveCountsAsAttendance;
        OvertimeRequiresApproval = overtimeRequiresApproval;
    }

    public void SetWeekendDays(string? weekendDays)
    {
        if (string.IsNullOrWhiteSpace(weekendDays)) { WeekendDays = ""; return; }
        var days = weekendDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.TryParse(d, out var v) ? v : -1)
            .Where(v => v is >= 0 and <= 6)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        WeekendDays = string.Join(',', days);
    }

    public bool IsWeekend(DayOfWeek day) => WeekendDays.Split(',').Contains(((int)day).ToString());

    public decimal OvertimeMultiplier(OvertimeCategory category) => category switch
    {
        OvertimeCategory.Weekend => OvertimeWeekendMultiplier,
        OvertimeCategory.Holiday => OvertimeHolidayMultiplier,
        _ => OvertimeNormalMultiplier
    };

    /// <summary>Hourly rate derived from a basic salary: salary / (working days per month x standard daily hours).</summary>
    public decimal HourlyRate(decimal basicSalary)
        => basicSalary <= 0 ? 0 : basicSalary / (StandardWorkingDaysPerMonth * StandardDailyHours);

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// One employee's attendance for one day (spec sections 30-31). Late / early-leave
/// / overtime minutes are DERIVED from the shift and the policy at recording time
/// and stored, so a later policy edit never rewrites history.
/// </summary>
public class AttendanceEvent : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public DateTime WorkDate { get; private set; }
    public Guid? HrShiftId { get; private set; }
    public string? ShiftName { get; private set; }

    /// <summary>Check-in as minutes from midnight; null means the employee did not attend.</summary>
    public int? CheckInMinutes { get; private set; }
    public int? CheckOutMinutes { get; private set; }

    public int WorkedMinutes { get; private set; }
    public int LateMinutes { get; private set; }
    public int EarlyLeaveMinutes { get; private set; }
    public int OvertimeMinutes { get; private set; }

    public AttendanceStatus Status { get; private set; } = AttendanceStatus.Present;
    public AttendanceSource Source { get; private set; } = AttendanceSource.Manual;

    /// <summary>When the day is covered by an approved leave request, its id.</summary>
    public Guid? LeaveRequestId { get; private set; }

    /// <summary>Days deducted for an unexcused absence (0 for every other status).</summary>
    public decimal AbsenceDays { get; private set; }

    public string? Notes { get; private set; }

    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private AttendanceEvent() { } // EF Core

    /// <summary>
    /// Records a day WITH check-in/out. Lateness, early leave, overtime and worked
    /// time are computed here from the shift and the policy - the client cannot
    /// send them, so they can never be forged.
    /// </summary>
    public AttendanceEvent(Guid employeeId, DateTime workDate, HrShift? shift, AttendanceRule rule,
        int checkInMinutes, int checkOutMinutes, AttendanceSource source, string createdBy, string? notes = null)
    {
        if (employeeId == Guid.Empty)
            throw new DomainException("An attendance record must name the employee.");
        if (checkInMinutes is < 0 or > 1439 || checkOutMinutes is < 0 or > 1439)
            throw new DomainException("Check-in and check-out must be valid times of day.");

        EmployeeId = employeeId;
        WorkDate = workDate.Date;
        HrShiftId = shift?.Id;
        ShiftName = shift?.Name;
        CheckInMinutes = checkInMinutes;
        CheckOutMinutes = checkOutMinutes;
        Source = source;
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;

        Compute(shift, rule);
    }

    /// <summary>Records a non-attendance day (absence, leave, holiday or rest day).</summary>
    public static AttendanceEvent NonWorking(
        Guid employeeId, DateTime workDate, AttendanceStatus status, HrShift? shift, AttendanceRule rule,
        string createdBy, Guid? leaveRequestId = null, string? notes = null)
    {
        if (status is AttendanceStatus.Present or AttendanceStatus.Late)
            throw new DomainException("Use the check-in/check-out overload to record a day that was worked.");

        var day = new AttendanceEvent
        {
            EmployeeId = employeeId,
            WorkDate = workDate.Date,
            HrShiftId = shift?.Id,
            ShiftName = shift?.Name,
            Source = AttendanceSource.Manual,
            Notes = Blank(notes),
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            Status = status,
            LeaveRequestId = leaveRequestId
        };

        day.AbsenceDays = status == AttendanceStatus.Absent ? 1m : 0m;
        return day;
    }

    /// <summary>Re-records a day (a corrected check-in/out) - the row is updated, never duplicated.</summary>
    public void ReRecord(HrShift? shift, AttendanceRule rule, int checkInMinutes, int checkOutMinutes,
        AttendanceSource source, string modifiedBy, string? notes = null)
    {
        if (checkInMinutes is < 0 or > 1439 || checkOutMinutes is < 0 or > 1439)
            throw new DomainException("Check-in and check-out must be valid times of day.");

        CheckInMinutes = checkInMinutes;
        CheckOutMinutes = checkOutMinutes;
        Source = source;
        HrShiftId = shift?.Id;
        ShiftName = shift?.Name;
        LeaveRequestId = null;
        AbsenceDays = 0m;
        Notes = Blank(notes);
        Compute(shift, rule);
        Touch(modifiedBy);
    }

    /// <summary>Marks the day as covered by an approved leave (the day is no longer an absence).</summary>
    public void MarkOnLeave(Guid leaveRequestId, string modifiedBy)
    {
        Status = AttendanceStatus.OnLeave;
        LeaveRequestId = leaveRequestId;
        AbsenceDays = 0m;
        WorkedMinutes = 0;
        LateMinutes = 0;
        EarlyLeaveMinutes = 0;
        OvertimeMinutes = 0;
        Touch(modifiedBy);
    }

    private void Compute(HrShift? shift, AttendanceRule rule)
    {
        var standardDailyMinutes = (int)Math.Round(rule.StandardDailyHours * 60m);
        var checkIn = CheckInMinutes!.Value;
        var checkOut = CheckOutMinutes!.Value;

        if (shift is null)
        {
            // No rostered shift: still record the worked time and any overtime beyond
            // the policy's standard day, but lateness/early leave are unknowable.
            var length = checkOut - checkIn;
            if (length < 0) length += 1440;
            WorkedMinutes = Math.Max(0, length);
            var beyond = Math.Max(0, WorkedMinutes - standardDailyMinutes);
            OvertimeMinutes = beyond > rule.OvertimeGraceMinutes ? beyond - rule.OvertimeGraceMinutes : 0;
        }
        else
        {
            var planned = shift.PlannedMinutes;
            var length = checkOut - checkIn;
            if (length < 0) length += 1440;
            WorkedMinutes = Math.Max(0, length - shift.BreakMinutes);

            // Lateness: only past the policy grace, then the whole delay counts.
            var sinceStart = checkIn - shift.StartMinutes;
            if (sinceStart < 0) sinceStart += 1440;
            LateMinutes = sinceStart > shift.GraceMinutes ? sinceStart : 0;

            // Early leave: leaving before the planned end.
            var sinceEnd = checkOut - shift.EndMinutes;
            if (shift.IsNightShift && sinceEnd < 0 && sinceEnd > -1440) sinceEnd += 1440;
            EarlyLeaveMinutes = sinceEnd < 0 ? Math.Min(-sinceEnd, planned) : 0;

            // Only the time BEYOND the overtime grace is overtime - the grace itself is not paid.
            OvertimeMinutes = sinceEnd > rule.OvertimeGraceMinutes ? sinceEnd - rule.OvertimeGraceMinutes : 0;
        }

        Status = LateMinutes > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
    }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// A configurable leave type (spec section 32): annual, sick, unpaid, maternity...
/// Whether it is paid and how many days are entitled is DATA, not a fixed list.
/// </summary>
public class LeaveType : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsPaid { get; private set; } = true;
    public decimal AnnualEntitlementDays { get; private set; }
    public bool RequiresApproval { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    private LeaveType() { } // EF Core

    public LeaveType(string code, string createdBy, string? nameAr = null, string? nameEn = null,
        bool isPaid = true, decimal annualEntitlementDays = 0m, bool requiresApproval = true)
    {
        SetCode(code);
        SetNames(nameAr, nameEn);
        IsPaid = isPaid;
        SetEntitlement(annualEntitlementDays);
        RequiresApproval = requiresApproval;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A leave type requires a code.");
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("A leave type requires a name (Arabic and/or English).");
        NameAr = (nameAr ?? nameEn)!.Trim();
        NameEn = (nameEn ?? nameAr)!.Trim();
        Name = NameEn;
    }

    public void SetEntitlement(decimal days)
    {
        if (days < 0)
            throw new DomainException("Annual entitlement cannot be negative.");
        AnnualEntitlementDays = days;
    }

    public void Configure(bool isPaid, decimal annualEntitlementDays, bool requiresApproval)
    {
        IsPaid = isPaid;
        SetEntitlement(annualEntitlementDays);
        RequiresApproval = requiresApproval;
    }

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// A leave request (spec section 32). Only an APPROVED request affects attendance
/// and payroll, and the requester can never decide their own request.
/// </summary>
public class LeaveRequest : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public DateTime FromDate { get; private set; }
    public DateTime ToDate { get; private set; }
    public decimal Days { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public LeaveStatus Status { get; private set; } = LeaveStatus.Pending;

    public string? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    /// <summary>True once the approved leave has been written into attendance, so it is applied exactly once.</summary>
    public bool AttendanceApplied { get; private set; }

    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private LeaveRequest() { } // EF Core

    public LeaveRequest(Guid employeeId, Guid leaveTypeId, DateTime fromDate, DateTime toDate,
        decimal days, string reason, string createdBy)
    {
        if (employeeId == Guid.Empty) throw new DomainException("A leave request must name the employee.");
        if (leaveTypeId == Guid.Empty) throw new DomainException("A leave request must name the leave type.");
        if (toDate.Date < fromDate.Date) throw new DomainException("The leave end date cannot precede its start date.");
        if (days <= 0) throw new DomainException("A leave request must cover at least one day.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A leave request must state a reason.");

        EmployeeId = employeeId;
        LeaveTypeId = leaveTypeId;
        FromDate = fromDate.Date;
        ToDate = toDate.Date;
        Days = days;
        Reason = reason.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Approve(string decidedBy, DateTime decidedAtUtc)
    {
        if (Status != LeaveStatus.Pending)
            throw new DomainException($"This leave request is already {Status} and cannot be decided again.");

        Status = LeaveStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = null;
        Touch(decidedBy);
    }

    public void Reject(string decidedBy, string reason, DateTime decidedAtUtc)
    {
        if (Status != LeaveStatus.Pending)
            throw new DomainException($"This leave request is already {Status} and cannot be decided again.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Rejecting a leave request requires a reason.");

        Status = LeaveStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = reason.Trim();
        Touch(decidedBy);
    }

    public void Cancel(string modifiedBy, string? reason = null)
    {
        if (Status == LeaveStatus.Rejected)
            throw new DomainException("A rejected leave request cannot be cancelled.");
        if (Status == LeaveStatus.Cancelled)
            throw new DomainException("This leave request is already cancelled.");

        Status = LeaveStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason)) DecisionReason = reason.Trim();
        AttendanceApplied = false;
        Touch(modifiedBy);
    }

    public void MarkAttendanceApplied(string modifiedBy)
    {
        AttendanceApplied = true;
        Touch(modifiedBy);
    }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// An overtime entry (spec section 33). Overtime hours are requested with a
/// category; the multiplier is snapshotted from the policy when it is APPROVED,
/// so a later policy edit never rewrites a paid figure.
/// </summary>
public class OvertimeEntry : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public DateTime WorkDate { get; private set; }
    public decimal Hours { get; private set; }
    public OvertimeCategory Category { get; private set; } = OvertimeCategory.Normal;
    public string Reason { get; private set; } = string.Empty;

    public OvertimeStatus Status { get; private set; } = OvertimeStatus.Pending;

    /// <summary>Snapshot of the policy multiplier applied at approval time (0 while pending).</summary>
    public decimal ApprovedMultiplier { get; private set; }

    public string? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    /// <summary>Set when a payroll run pays this entry, so it is never paid twice.</summary>
    public Guid? PayrollRunId { get; private set; }

    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private OvertimeEntry() { } // EF Core

    public OvertimeEntry(Guid employeeId, DateTime workDate, decimal hours, OvertimeCategory category,
        string reason, string createdBy, OvertimeStatus status = OvertimeStatus.Pending)
    {
        if (employeeId == Guid.Empty) throw new DomainException("An overtime entry must name the employee.");
        if (hours <= 0 || hours > 24) throw new DomainException("Overtime hours must be between 0 and 24.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("An overtime entry must state a reason.");

        EmployeeId = employeeId;
        WorkDate = workDate.Date;
        Hours = hours;
        Category = category;
        Reason = reason.Trim();
        Status = status;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Approve(string decidedBy, decimal multiplier, DateTime decidedAtUtc)
    {
        if (Status != OvertimeStatus.Pending)
            throw new DomainException($"This overtime entry is already {Status} and cannot be decided again.");
        if (multiplier <= 0)
            throw new DomainException("An overtime multiplier must be positive.");

        Status = OvertimeStatus.Approved;
        ApprovedMultiplier = multiplier;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = null;
        Touch(decidedBy);
    }

    public void Reject(string decidedBy, string reason, DateTime decidedAtUtc)
    {
        if (Status != OvertimeStatus.Pending)
            throw new DomainException($"This overtime entry is already {Status} and cannot be decided again.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Rejecting an overtime entry requires a reason.");

        Status = OvertimeStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        DecisionReason = reason.Trim();
        Touch(decidedBy);
    }

    public void MarkPaid(Guid payrollRunId, string modifiedBy)
    {
        if (Status != OvertimeStatus.Approved)
            throw new DomainException("Only an approved overtime entry can be paid.");
        if (PayrollRunId.HasValue && PayrollRunId.Value != payrollRunId)
            throw new DomainException("This overtime entry has already been paid by another payroll run.");
        PayrollRunId = payrollRunId;
        Touch(modifiedBy);
    }

    /// <summary>
    /// Releases the entry back to "approved and unpaid" when the run that paid it
    /// is CANCELLED or REVERSED. Without this the hours would stay attached to a
    /// dead run and could never be paid again. Only the run that actually paid the
    /// entry may release it, and a released entry keeps its approved multiplier.
    /// </summary>
    public void ReleasePayment(Guid payrollRunId, string modifiedBy)
    {
        if (!PayrollRunId.HasValue || PayrollRunId.Value != payrollRunId) return;

        PayrollRunId = null;
        Touch(modifiedBy);
    }

    private void Touch(string modifiedBy) { ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
}

/// <summary>
/// An employee advance/salary loan (spec section 34): paid out once, then recovered
/// from payroll in configured instalments until it is closed. Paying advances out
/// of the treasury is intentionally out of scope here - the recovery period is
/// what payroll needs.
/// </summary>
public class EmployeeAdvance : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public DateTime AdvanceDate { get; private set; }
    public decimal Amount { get; private set; }
    public int InstalmentCount { get; private set; }
    public decimal InstalmentAmount { get; private set; }
    public decimal RecoveredAmount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public AdvanceStatus Status { get; private set; } = AdvanceStatus.Active;

    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public decimal RemainingAmount => Math.Max(0, Amount - RecoveredAmount);

    private EmployeeAdvance() { } // EF Core

    public EmployeeAdvance(Guid employeeId, DateTime advanceDate, decimal amount, int instalmentCount,
        string reason, string createdBy)
    {
        if (employeeId == Guid.Empty) throw new DomainException("An advance must name the employee.");
        if (amount <= 0) throw new DomainException("An advance amount must be positive.");
        if (instalmentCount <= 0) throw new DomainException("An advance must be recovered in at least one instalment.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("An advance must state a reason.");

        EmployeeId = employeeId;
        AdvanceDate = advanceDate.Date;
        Amount = amount;
        InstalmentCount = instalmentCount;
        InstalmentAmount = Math.Round(amount / instalmentCount, 4, MidpointRounding.AwayFromZero);
        Reason = reason.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Recovers one payroll instalment (never more than what remains). Callers pass the scheduled amount.</summary>
    public decimal Recover(decimal requested, string modifiedBy)
    {
        if (Status != AdvanceStatus.Active)
            throw new DomainException("Only an active advance can be recovered.");
        if (requested <= 0) return 0m;

        var taken = Math.Min(requested, RemainingAmount);
        RecoveredAmount += taken;
        if (RemainingAmount <= 0) Status = AdvanceStatus.Closed;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
        return taken;
    }

    /// <summary>
    /// Gives back an instalment that was recovered by a payroll run which was then
    /// CANCELLED or REVERSED. The sum never goes below zero, a cancelled advance is
    /// left alone (it owes nothing), and a fully recovered advance reopens as soon
    /// as it owes again - otherwise the reversal would silently swallow the money.
    /// </summary>
    public void Release(decimal requested, string modifiedBy)
    {
        if (Status == AdvanceStatus.Cancelled || requested <= 0) return;

        RecoveredAmount = Math.Max(0m, RecoveredAmount - requested);
        if (RemainingAmount > 0) Status = AdvanceStatus.Active;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string modifiedBy, string reason)
    {
        if (Status == AdvanceStatus.Closed)
            throw new DomainException("A fully recovered advance cannot be cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelling an advance requires a reason.");

        Status = AdvanceStatus.Cancelled;
        ModifiedBy = $"{modifiedBy} ({reason.Trim()})";
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
