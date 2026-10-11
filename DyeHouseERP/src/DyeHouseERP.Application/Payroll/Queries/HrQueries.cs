using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Queries;

/// <summary>Shared entity -&gt; DTO mapping for the HR workflow records.</summary>
public static class HrDtoBuilder
{
    public static LeaveRequestDto MapLeave(LeaveRequest l, Employee? employee, string leaveTypeName, bool isPaid) => new()
    {
        Id = l.Id, EmployeeId = l.EmployeeId, EmployeeCode = employee?.Code ?? "", EmployeeName = employee?.Name ?? "",
        LeaveTypeId = l.LeaveTypeId, LeaveTypeName = leaveTypeName, IsPaid = isPaid,
        FromDate = l.FromDate, ToDate = l.ToDate, Days = l.Days, Reason = l.Reason, Status = l.Status,
        DecidedBy = l.DecidedBy, DecidedAtUtc = l.DecidedAtUtc, DecisionReason = l.DecisionReason,
        AttendanceApplied = l.AttendanceApplied, CreatedBy = l.CreatedBy, CreatedAtUtc = l.CreatedAtUtc
    };

    public static OvertimeEntryDto MapOvertime(OvertimeEntry o, Employee? employee) => new()
    {
        Id = o.Id, EmployeeId = o.EmployeeId, EmployeeCode = employee?.Code ?? "", EmployeeName = employee?.Name ?? "",
        WorkDate = o.WorkDate, Hours = o.Hours, Category = o.Category, Reason = o.Reason, Status = o.Status,
        ApprovedMultiplier = o.ApprovedMultiplier, DecidedBy = o.DecidedBy, DecidedAtUtc = o.DecidedAtUtc,
        DecisionReason = o.DecisionReason, PayrollRunId = o.PayrollRunId
    };

    public static EmployeeAdvanceDto MapAdvance(EmployeeAdvance a, Employee? employee) => new()
    {
        Id = a.Id, EmployeeId = a.EmployeeId, EmployeeCode = employee?.Code ?? "", EmployeeName = employee?.Name ?? "",
        AdvanceDate = a.AdvanceDate, Amount = a.Amount, InstalmentCount = a.InstalmentCount,
        InstalmentAmount = a.InstalmentAmount, RecoveredAmount = a.RecoveredAmount,
        RemainingAmount = a.RemainingAmount, Reason = a.Reason, Status = a.Status
    };
}

// --------------------------------------------------------------- master lists

public record GetHrSectionsQuery(Guid? DepartmentId = null, bool? ActiveOnly = null) : IRequest<List<HrSectionDto>>;

public class GetHrSectionsQueryHandler : IRequestHandler<GetHrSectionsQuery, List<HrSectionDto>>
{
    private readonly IApplicationDbContext _db;
    public GetHrSectionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<HrSectionDto>> Handle(GetHrSectionsQuery r, CancellationToken ct)
    {
        var query = _db.HrSections.AsNoTracking().AsQueryable();
        if (r.DepartmentId.HasValue) query = query.Where(s => s.DepartmentId == r.DepartmentId);
        if (r.ActiveOnly == true) query = query.Where(s => s.IsActive);

        var sections = await query.OrderBy(s => s.Code).ToListAsync(ct);
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return sections.Select(s => HrMapping.Map(s, departments.GetValueOrDefault(s.DepartmentId) ?? "")).ToList();
    }
}

public record GetJobPositionsQuery(Guid? DepartmentId = null, bool? ActiveOnly = null) : IRequest<List<JobPositionDto>>;

public class GetJobPositionsQueryHandler : IRequestHandler<GetJobPositionsQuery, List<JobPositionDto>>
{
    private readonly IApplicationDbContext _db;
    public GetJobPositionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<JobPositionDto>> Handle(GetJobPositionsQuery r, CancellationToken ct)
    {
        var query = _db.JobPositions.AsNoTracking().AsQueryable();
        if (r.DepartmentId.HasValue) query = query.Where(j => j.DepartmentId == r.DepartmentId);
        if (r.ActiveOnly == true) query = query.Where(j => j.IsActive);

        var jobs = await query.OrderBy(j => j.Code).ToListAsync(ct);
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return jobs.Select(j => HrMapping.Map(j, j.DepartmentId.HasValue ? departments.GetValueOrDefault(j.DepartmentId.Value) : null)).ToList();
    }
}

public record GetCostCentersQuery(bool? ActiveOnly = null) : IRequest<List<CostCenterDto>>;

public class GetCostCentersQueryHandler : IRequestHandler<GetCostCentersQuery, List<CostCenterDto>>
{
    private readonly IApplicationDbContext _db;
    public GetCostCentersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<CostCenterDto>> Handle(GetCostCentersQuery r, CancellationToken ct)
    {
        var query = _db.CostCenters.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(c => c.IsActive);
        return (await query.OrderBy(c => c.Code).ToListAsync(ct)).Select(HrMapping.Map).ToList();
    }
}

public record GetHrShiftsQuery(bool? ActiveOnly = null) : IRequest<List<HrShiftDto>>;

public class GetHrShiftsQueryHandler : IRequestHandler<GetHrShiftsQuery, List<HrShiftDto>>
{
    private readonly IApplicationDbContext _db;
    public GetHrShiftsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<HrShiftDto>> Handle(GetHrShiftsQuery r, CancellationToken ct)
    {
        var query = _db.HrShifts.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(s => s.IsActive);
        return (await query.OrderBy(s => s.Code).ToListAsync(ct)).Select(HrMapping.Map).ToList();
    }
}

public record GetAttendanceRulesQuery(bool? ActiveOnly = null) : IRequest<List<AttendanceRuleDto>>;

public class GetAttendanceRulesQueryHandler : IRequestHandler<GetAttendanceRulesQuery, List<AttendanceRuleDto>>
{
    private readonly IApplicationDbContext _db;
    public GetAttendanceRulesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AttendanceRuleDto>> Handle(GetAttendanceRulesQuery r, CancellationToken ct)
    {
        var query = _db.AttendanceRules.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(a => a.IsActive);
        return (await query.OrderByDescending(a => a.IsActive).ThenBy(a => a.Name).ToListAsync(ct))
            .Select(HrMapping.Map).ToList();
    }
}

public record GetLeaveTypesQuery(bool? ActiveOnly = null) : IRequest<List<LeaveTypeDto>>;

public class GetLeaveTypesQueryHandler : IRequestHandler<GetLeaveTypesQuery, List<LeaveTypeDto>>
{
    private readonly IApplicationDbContext _db;
    public GetLeaveTypesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<LeaveTypeDto>> Handle(GetLeaveTypesQuery r, CancellationToken ct)
    {
        var query = _db.LeaveTypes.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(t => t.IsActive);
        return (await query.OrderBy(t => t.Code).ToListAsync(ct)).Select(HrMapping.Map).ToList();
    }
}

public record GetPayComponentsQuery(bool? ActiveOnly = null) : IRequest<List<PayComponentDto>>;

public class GetPayComponentsQueryHandler : IRequestHandler<GetPayComponentsQuery, List<PayComponentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPayComponentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PayComponentDto>> Handle(GetPayComponentsQuery r, CancellationToken ct)
    {
        var query = _db.PayComponents.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(c => c.IsActive);
        return (await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Code).ToListAsync(ct))
            .Select(HrMapping.Map).ToList();
    }
}

public record GetContributionRulesQuery(bool? ActiveOnly = null) : IRequest<List<ContributionRuleDto>>;

public class GetContributionRulesQueryHandler : IRequestHandler<GetContributionRulesQuery, List<ContributionRuleDto>>
{
    private readonly IApplicationDbContext _db;
    public GetContributionRulesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ContributionRuleDto>> Handle(GetContributionRulesQuery r, CancellationToken ct)
    {
        var query = _db.ContributionRules.AsNoTracking().AsQueryable();
        if (r.ActiveOnly == true) query = query.Where(c => c.IsActive);
        return (await query.OrderBy(c => c.Code).ToListAsync(ct)).Select(HrMapping.Map).ToList();
    }
}

public record GetPayrollAccountMappingsQuery() : IRequest<List<PayrollAccountMappingDto>>;

public class GetPayrollAccountMappingsQueryHandler : IRequestHandler<GetPayrollAccountMappingsQuery, List<PayrollAccountMappingDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPayrollAccountMappingsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PayrollAccountMappingDto>> Handle(GetPayrollAccountMappingsQuery r, CancellationToken ct)
        => (await _db.PayrollAccountMappings.AsNoTracking().OrderBy(m => m.Kind).ToListAsync(ct))
            .Select(HrMapping.Map).ToList();
}

public record GetEmployeePayComponentsQuery(Guid? EmployeeId = null, bool? ActiveOnly = null)
    : IRequest<List<EmployeePayComponentDto>>;

public class GetEmployeePayComponentsQueryHandler : IRequestHandler<GetEmployeePayComponentsQuery, List<EmployeePayComponentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetEmployeePayComponentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<EmployeePayComponentDto>> Handle(GetEmployeePayComponentsQuery r, CancellationToken ct)
    {
        var query = _db.EmployeePayComponents.AsNoTracking().AsQueryable();
        if (r.EmployeeId.HasValue) query = query.Where(a => a.EmployeeId == r.EmployeeId);
        if (r.ActiveOnly == true) query = query.Where(a => a.IsActive);

        var assignments = await query.OrderByDescending(a => a.EffectiveFrom).ToListAsync(ct);
        var employees = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, e => (e.Code, e.Name), ct);
        var components = await _db.PayComponents.AsNoTracking().ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct);

        return assignments.Select(a => new EmployeePayComponentDto
        {
            Id = a.Id, EmployeeId = a.EmployeeId,
            EmployeeCode = employees.TryGetValue(a.EmployeeId, out var e) ? e.Code : "",
            EmployeeName = employees.TryGetValue(a.EmployeeId, out var e2) ? e2.Name : "",
            PayComponentId = a.PayComponentId,
            PayComponentCode = components.TryGetValue(a.PayComponentId, out var c) ? c.Code : "",
            PayComponentName = components.TryGetValue(a.PayComponentId, out var c2) ? c2.Name : "",
            Amount = a.Amount, Percent = a.Percent, EffectiveFrom = a.EffectiveFrom, EffectiveTo = a.EffectiveTo,
            IsActive = a.IsActive
        }).ToList();
    }
}

// ---------------------------------------------------- attendance / leave / overtime

public record GetAttendanceQuery(Guid? EmployeeId = null, DateTime? FromDate = null, DateTime? ToDate = null,
    AttendanceStatus? Status = null, Guid? DepartmentId = null) : IRequest<List<AttendanceEventDto>>;

public class GetAttendanceQueryHandler : IRequestHandler<GetAttendanceQuery, List<AttendanceEventDto>>
{
    private readonly IApplicationDbContext _db;
    public GetAttendanceQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AttendanceEventDto>> Handle(GetAttendanceQuery r, CancellationToken ct)
    {
        var query = _db.AttendanceEvents.AsNoTracking().AsQueryable();
        if (r.EmployeeId.HasValue) query = query.Where(a => a.EmployeeId == r.EmployeeId);
        if (r.FromDate.HasValue) query = query.Where(a => a.WorkDate >= r.FromDate.Value.Date);
        if (r.ToDate.HasValue) query = query.Where(a => a.WorkDate <= r.ToDate.Value.Date);
        if (r.Status.HasValue) query = query.Where(a => a.Status == r.Status);

        var events = await query.OrderByDescending(a => a.WorkDate).ThenBy(a => a.EmployeeId)
            .Take(2000).ToListAsync(ct);

        var employeeIds = events.Select(e => e.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        // A department filter narrows the rows to that department's employees.
        if (r.DepartmentId.HasValue)
            events = events.Where(e => employees.TryGetValue(e.EmployeeId, out var emp)
                && emp.DepartmentId == r.DepartmentId.Value).ToList();

        return events.Select(e => HrLookup.Map(e, employees.GetValueOrDefault(e.EmployeeId))).ToList();
    }
}

public record GetAttendanceSummaryQuery(DateTime FromDate, DateTime ToDate, Guid? DepartmentId = null)
    : IRequest<List<AttendanceSummaryDto>>;

public class GetAttendanceSummaryQueryHandler : IRequestHandler<GetAttendanceSummaryQuery, List<AttendanceSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetAttendanceSummaryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AttendanceSummaryDto>> Handle(GetAttendanceSummaryQuery r, CancellationToken ct)
    {
        var employeesQuery = _db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (r.DepartmentId.HasValue) employeesQuery = employeesQuery.Where(e => e.DepartmentId == r.DepartmentId.Value);
        var employees = await employeesQuery.OrderBy(e => e.Code).ToListAsync(ct);
        var ids = employees.Select(e => e.Id).ToList();

        var events = await _db.AttendanceEvents.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.WorkDate >= r.FromDate.Date && a.WorkDate <= r.ToDate.Date)
            .ToListAsync(ct);

        return employees.Select(employee =>
        {
            var mine = events.Where(e => e.EmployeeId == employee.Id).ToList();
            return new AttendanceSummaryDto
            {
                EmployeeId = employee.Id, EmployeeCode = employee.Code, EmployeeName = employee.Name,
                PresentDays = mine.Count(e => e.Status is AttendanceStatus.Present or AttendanceStatus.Late),
                LateDays = mine.Count(e => e.Status == AttendanceStatus.Late),
                AbsentDays = mine.Count(e => e.Status == AttendanceStatus.Absent),
                LeaveDays = mine.Count(e => e.Status == AttendanceStatus.OnLeave),
                LateMinutes = mine.Sum(e => e.LateMinutes),
                OvertimeMinutes = mine.Sum(e => e.OvertimeMinutes),
                WorkedHours = Math.Round(mine.Sum(e => e.WorkedMinutes) / 60m, 2)
            };
        }).ToList();
    }
}

public record GetLeaveRequestsQuery(LeaveStatus? Status = null, Guid? EmployeeId = null) : IRequest<List<LeaveRequestDto>>;

public class GetLeaveRequestsQueryHandler : IRequestHandler<GetLeaveRequestsQuery, List<LeaveRequestDto>>
{
    private readonly IApplicationDbContext _db;
    public GetLeaveRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<LeaveRequestDto>> Handle(GetLeaveRequestsQuery r, CancellationToken ct)
    {
        var query = _db.LeaveRequests.AsNoTracking().AsQueryable();
        if (r.Status.HasValue) query = query.Where(l => l.Status == r.Status);
        if (r.EmployeeId.HasValue) query = query.Where(l => l.EmployeeId == r.EmployeeId);

        var requests = await query.OrderByDescending(l => l.FromDate).Take(1000).ToListAsync(ct);
        var employees = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, ct);
        var types = await _db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);

        return requests.Select(l =>
        {
            employees.TryGetValue(l.EmployeeId, out var employee);
            types.TryGetValue(l.LeaveTypeId, out var type);
            return HrDtoBuilder.MapLeave(l, employee, type?.Name ?? "", type?.IsPaid ?? false);
        }).ToList();
    }
}

public record GetOvertimeEntriesQuery(OvertimeStatus? Status = null, Guid? EmployeeId = null,
    DateTime? FromDate = null, DateTime? ToDate = null) : IRequest<List<OvertimeEntryDto>>;

public class GetOvertimeEntriesQueryHandler : IRequestHandler<GetOvertimeEntriesQuery, List<OvertimeEntryDto>>
{
    private readonly IApplicationDbContext _db;
    public GetOvertimeEntriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<OvertimeEntryDto>> Handle(GetOvertimeEntriesQuery r, CancellationToken ct)
    {
        var query = _db.OvertimeEntries.AsNoTracking().AsQueryable();
        if (r.Status.HasValue) query = query.Where(o => o.Status == r.Status);
        if (r.EmployeeId.HasValue) query = query.Where(o => o.EmployeeId == r.EmployeeId);
        if (r.FromDate.HasValue) query = query.Where(o => o.WorkDate >= r.FromDate.Value.Date);
        if (r.ToDate.HasValue) query = query.Where(o => o.WorkDate <= r.ToDate.Value.Date);

        var entries = await query.OrderByDescending(o => o.WorkDate).Take(1000).ToListAsync(ct);
        var employees = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, ct);
        return entries.Select(o => HrDtoBuilder.MapOvertime(o, employees.GetValueOrDefault(o.EmployeeId))).ToList();
    }
}

public record GetEmployeeAdvancesQuery(Guid? EmployeeId = null, AdvanceStatus? Status = null)
    : IRequest<List<EmployeeAdvanceDto>>;

public class GetEmployeeAdvancesQueryHandler : IRequestHandler<GetEmployeeAdvancesQuery, List<EmployeeAdvanceDto>>
{
    private readonly IApplicationDbContext _db;
    public GetEmployeeAdvancesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<EmployeeAdvanceDto>> Handle(GetEmployeeAdvancesQuery r, CancellationToken ct)
    {
        var query = _db.EmployeeAdvances.AsNoTracking().AsQueryable();
        if (r.EmployeeId.HasValue) query = query.Where(a => a.EmployeeId == r.EmployeeId);
        if (r.Status.HasValue) query = query.Where(a => a.Status == r.Status);

        var advances = await query.OrderByDescending(a => a.AdvanceDate).Take(1000).ToListAsync(ct);
        var employees = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, ct);
        return advances.Select(a => HrDtoBuilder.MapAdvance(a, employees.GetValueOrDefault(a.EmployeeId))).ToList();
    }
}

// ------------------------------------------------------------- labor-cost report

/// <summary>
/// Labor-cost report for one payroll period (spec section 40): the wage bill grouped
/// by department or cost center, including the employer's contribution so the row
/// shows the true cost to the factory, not just what the employee takes home.
/// </summary>
public record GetLaborCostReportQuery(int PeriodYear, int PeriodMonth, bool GroupByCostCenter = false)
    : IRequest<LaborCostReportDto>;

public class GetLaborCostReportQueryHandler : IRequestHandler<GetLaborCostReportQuery, LaborCostReportDto>
{
    private readonly IApplicationDbContext _db;
    public GetLaborCostReportQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<LaborCostReportDto> Handle(GetLaborCostReportQuery r, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.PeriodYear == r.PeriodYear && x.PeriodMonth == r.PeriodMonth
                        && x.Status != PayrollRunStatus.Cancelled && x.ReversesPayrollRunId == null)
            .OrderByDescending(x => x.Status)
            .FirstOrDefaultAsync(ct);

        // No run for that period is not an error: the report is simply empty, which is
        // what a dashboard should show for an unprocessed month.
        if (run is null)
            return new LaborCostReportDto { PeriodYear = r.PeriodYear, PeriodMonth = r.PeriodMonth, Rows = new List<LaborCostRowDto>() };

        var departmentIds = run.Lines.Select(l => l.DepartmentId).Distinct().ToList();
        var departments = await _db.Departments.AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var costCenterIds = run.Lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        var costCenters = await _db.CostCenters.AsNoTracking()
            .Where(c => costCenterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var groups = run.Lines.GroupBy(l => r.GroupByCostCenter
            ? (Key: l.CostCenterId, Name: l.CostCenterId.HasValue ? costCenters.GetValueOrDefault(l.CostCenterId.Value) : null)
            : (Key: (Guid?)l.DepartmentId, Name: departments.GetValueOrDefault(l.DepartmentId)));

        var rows = groups.Select(g => new LaborCostRowDto
        {
            GroupKey = g.Key.Key?.ToString() ?? "unassigned",
            DepartmentId = r.GroupByCostCenter ? null : g.Key.Key,
            DepartmentName = r.GroupByCostCenter ? null : g.Key.Name,
            CostCenterId = r.GroupByCostCenter ? g.Key.Key : null,
            CostCenterName = r.GroupByCostCenter ? g.Key.Name : null,
            EmployeeCount = g.Count(),
            BasicSalary = g.Sum(l => l.BasicSalary),
            Allowances = g.Sum(l => l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OtherEarnings),
            Overtime = g.Sum(l => l.OvertimeAmount),
            Deductions = g.Sum(l => l.Deductions),
            Tax = g.Sum(l => l.Tax),
            Insurance = g.Sum(l => l.Insurance),
            EmployerInsurance = g.Sum(l => l.EmployerInsurance),
            Advances = g.Sum(l => l.Advances),
            Gross = g.Sum(l => l.GrossPay),
            Net = g.Sum(l => l.NetPay),
            EmployerCost = g.Sum(l => l.GrossPay + l.EmployerInsurance)
        }).OrderByDescending(x => x.EmployerCost).ToList();

        return new LaborCostReportDto
        {
            PeriodYear = r.PeriodYear,
            PeriodMonth = r.PeriodMonth,
            Rows = rows,
            TotalBasicSalary = rows.Sum(x => x.BasicSalary),
            TotalGross = rows.Sum(x => x.Gross),
            TotalNet = rows.Sum(x => x.Net),
            TotalEmployerCost = rows.Sum(x => x.EmployerCost)
        };
    }
}
