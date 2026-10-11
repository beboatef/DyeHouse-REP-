using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>
/// Entity -&gt; DTO mapping for the HR/payroll masters, shared by commands and
/// queries so a create response and a list response can never disagree.
/// </summary>
public static class HrMapping
{
    public static HrSectionDto Map(HrSection s, string departmentName) => new()
    {
        Id = s.Id, DepartmentId = s.DepartmentId, DepartmentName = departmentName, Code = s.Code,
        Name = s.Name, NameAr = s.NameAr, NameEn = s.NameEn, Notes = s.Notes, IsActive = s.IsActive
    };

    public static JobPositionDto Map(JobPosition j, string? departmentName) => new()
    {
        Id = j.Id, DepartmentId = j.DepartmentId, DepartmentName = departmentName, Code = j.Code,
        Name = j.Name, NameAr = j.NameAr, NameEn = j.NameEn, DefaultBasicSalary = j.DefaultBasicSalary,
        Notes = j.Notes, IsActive = j.IsActive
    };

    public static CostCenterDto Map(CostCenter c) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, NameAr = c.NameAr, NameEn = c.NameEn, Notes = c.Notes, IsActive = c.IsActive
    };

    public static HrShiftDto Map(HrShift s) => new()
    {
        Id = s.Id, Code = s.Code, Name = s.Name, NameAr = s.NameAr, NameEn = s.NameEn,
        StartMinutes = s.StartMinutes, EndMinutes = s.EndMinutes, BreakMinutes = s.BreakMinutes,
        GraceMinutes = s.GraceMinutes, WorkingDays = s.WorkingDays, IsNightShift = s.IsNightShift,
        IsActive = s.IsActive, PlannedMinutes = s.PlannedMinutes
    };

    public static AttendanceRuleDto Map(AttendanceRule r) => new()
    {
        Id = r.Id, Name = r.Name, StandardDailyHours = r.StandardDailyHours,
        StandardWorkingDaysPerMonth = r.StandardWorkingDaysPerMonth, GraceMinutes = r.GraceMinutes,
        LatePenaltyPerMinute = r.LatePenaltyPerMinute, EarlyLeavePenaltyPerMinute = r.EarlyLeavePenaltyPerMinute,
        AbsenceDayDeductionFactor = r.AbsenceDayDeductionFactor, OvertimeGraceMinutes = r.OvertimeGraceMinutes,
        OvertimeNormalMultiplier = r.OvertimeNormalMultiplier, OvertimeWeekendMultiplier = r.OvertimeWeekendMultiplier,
        OvertimeHolidayMultiplier = r.OvertimeHolidayMultiplier, WeekendDays = r.WeekendDays,
        PaidLeaveCountsAsAttendance = r.PaidLeaveCountsAsAttendance,
        OvertimeRequiresApproval = r.OvertimeRequiresApproval, IsActive = r.IsActive
    };

    public static LeaveTypeDto Map(LeaveType t) => new()
    {
        Id = t.Id, Code = t.Code, Name = t.Name, NameAr = t.NameAr, NameEn = t.NameEn,
        IsPaid = t.IsPaid, AnnualEntitlementDays = t.AnnualEntitlementDays,
        RequiresApproval = t.RequiresApproval, IsActive = t.IsActive
    };

    public static PayComponentDto Map(PayComponent c) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, NameAr = c.NameAr, NameEn = c.NameEn,
        Kind = c.Kind, Calculation = c.Calculation, DefaultAmount = c.DefaultAmount,
        DefaultPercent = c.DefaultPercent, IsTaxable = c.IsTaxable, IsInsured = c.IsInsured,
        SortOrder = c.SortOrder, Notes = c.Notes, IsActive = c.IsActive
    };

    public static ContributionRuleDto Map(ContributionRule r) => new()
    {
        Id = r.Id, Code = r.Code, Name = r.Name, NameAr = r.NameAr, NameEn = r.NameEn,
        Kind = r.Kind, Base = r.Base, EmployeeRatePercent = r.EmployeeRatePercent,
        EmployerRatePercent = r.EmployerRatePercent, MinBase = r.MinBase, MaxBase = r.MaxBase, IsActive = r.IsActive
    };

    public static PayrollAccountMappingDto Map(PayrollAccountMapping m) => new()
    {
        Id = m.Id, Kind = m.Kind, AccountCode = m.AccountCode, AccountName = m.AccountName,
        Notes = m.Notes, IsActive = m.IsActive
    };
}

// ------------------------------------------------------------------- sections

public record CreateHrSectionCommand(Guid DepartmentId, string Code, string? NameAr = null, string? NameEn = null,
    string? Notes = null) : IRequest<HrSectionDto>;

public class CreateHrSectionCommandValidator : AbstractValidator<CreateHrSectionCommand>
{
    public CreateHrSectionCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A section name is required (Arabic and/or English).");
    }
}

public class CreateHrSectionCommandHandler : IRequestHandler<CreateHrSectionCommand, HrSectionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateHrSectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<HrSectionDto> Handle(CreateHrSectionCommand r, CancellationToken ct)
    {
        var department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == r.DepartmentId, ct)
            ?? throw new NotFoundException("Department", r.DepartmentId);

        var code = r.Code.Trim();
        if (await _db.HrSections.AnyAsync(s => s.Code == code, ct))
            throw new DuplicateCodeException("HrSection", code);

        var section = new HrSection(r.DepartmentId, code, _currentUser.UserName, r.NameAr, r.NameEn, r.Notes);
        _db.HrSections.Add(section);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(section, department.Name);
    }
}

public record UpdateHrSectionCommand(Guid Id, Guid? DepartmentId = null, string? NameAr = null, string? NameEn = null,
    string? Notes = null, bool? IsActive = null) : IRequest<HrSectionDto>;

public class UpdateHrSectionCommandHandler : IRequestHandler<UpdateHrSectionCommand, HrSectionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateHrSectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<HrSectionDto> Handle(UpdateHrSectionCommand r, CancellationToken ct)
    {
        var section = await _db.HrSections.FirstOrDefaultAsync(s => s.Id == r.Id, ct)
            ?? throw new NotFoundException("HrSection", r.Id);

        if (r.DepartmentId.HasValue)
        {
            if (!await _db.Departments.AnyAsync(d => d.Id == r.DepartmentId.Value, ct))
                throw new NotFoundException("Department", r.DepartmentId.Value);
            section.SetDepartment(r.DepartmentId.Value);
        }
        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            section.SetNames(r.NameAr, r.NameEn);
        if (r.Notes is not null) section.SetNotes(r.Notes);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) section.Activate(_currentUser.UserName);
            else section.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        var departmentName = await _db.Departments.AsNoTracking()
            .Where(d => d.Id == section.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct) ?? "";
        return HrMapping.Map(section, departmentName);
    }
}

// --------------------------------------------------------------- job positions

public record CreateJobPositionCommand(string Code, string? NameAr = null, string? NameEn = null,
    Guid? DepartmentId = null, decimal? DefaultBasicSalary = null, string? Notes = null) : IRequest<JobPositionDto>;

public class CreateJobPositionCommandValidator : AbstractValidator<CreateJobPositionCommand>
{
    public CreateJobPositionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.DefaultBasicSalary).GreaterThanOrEqualTo(0).When(x => x.DefaultBasicSalary.HasValue);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A job name is required (Arabic and/or English).");
    }
}

public class CreateJobPositionCommandHandler : IRequestHandler<CreateJobPositionCommand, JobPositionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateJobPositionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<JobPositionDto> Handle(CreateJobPositionCommand r, CancellationToken ct)
    {
        if (r.DepartmentId.HasValue && !await _db.Departments.AnyAsync(d => d.Id == r.DepartmentId.Value, ct))
            throw new NotFoundException("Department", r.DepartmentId.Value);

        var code = r.Code.Trim();
        if (await _db.JobPositions.AnyAsync(j => j.Code == code, ct))
            throw new DuplicateCodeException("JobPosition", code);

        var job = new JobPosition(code, _currentUser.UserName, r.NameAr, r.NameEn, r.DepartmentId,
            r.DefaultBasicSalary, r.Notes);
        _db.JobPositions.Add(job);
        await _db.SaveChangesAsync(ct);

        var departmentName = job.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == job.DepartmentId.Value).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;
        return HrMapping.Map(job, departmentName);
    }
}

public record UpdateJobPositionCommand(Guid Id, string? NameAr = null, string? NameEn = null, Guid? DepartmentId = null,
    bool ClearDepartment = false, decimal? DefaultBasicSalary = null, string? Notes = null, bool? IsActive = null)
    : IRequest<JobPositionDto>;

public class UpdateJobPositionCommandHandler : IRequestHandler<UpdateJobPositionCommand, JobPositionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateJobPositionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<JobPositionDto> Handle(UpdateJobPositionCommand r, CancellationToken ct)
    {
        var job = await _db.JobPositions.FirstOrDefaultAsync(j => j.Id == r.Id, ct)
            ?? throw new NotFoundException("JobPosition", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            job.SetNames(r.NameAr, r.NameEn);
        if (r.DepartmentId.HasValue)
        {
            if (!await _db.Departments.AnyAsync(d => d.Id == r.DepartmentId.Value, ct))
                throw new NotFoundException("Department", r.DepartmentId.Value);
            job.SetDepartment(r.DepartmentId.Value);
        }
        else if (r.ClearDepartment) job.SetDepartment(null);
        if (r.DefaultBasicSalary.HasValue) job.SetDefaultSalary(r.DefaultBasicSalary);
        if (r.Notes is not null) job.SetNotes(r.Notes);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) job.Activate(_currentUser.UserName);
            else job.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        var departmentName = job.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == job.DepartmentId.Value).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;
        return HrMapping.Map(job, departmentName);
    }
}

// ---------------------------------------------------------------- cost centers

public record CreateCostCenterCommand(string Code, string? NameAr = null, string? NameEn = null, string? Notes = null)
    : IRequest<CostCenterDto>;

public class CreateCostCenterCommandValidator : AbstractValidator<CreateCostCenterCommand>
{
    public CreateCostCenterCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A cost center name is required (Arabic and/or English).");
    }
}

public class CreateCostCenterCommandHandler : IRequestHandler<CreateCostCenterCommand, CostCenterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateCostCenterCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<CostCenterDto> Handle(CreateCostCenterCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        if (await _db.CostCenters.AnyAsync(c => c.Code == code, ct))
            throw new DuplicateCodeException("CostCenter", code);

        var costCenter = new CostCenter(code, _currentUser.UserName, r.NameAr, r.NameEn, r.Notes);
        _db.CostCenters.Add(costCenter);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(costCenter);
    }
}

public record UpdateCostCenterCommand(Guid Id, string? NameAr = null, string? NameEn = null, string? Notes = null,
    bool? IsActive = null) : IRequest<CostCenterDto>;

public class UpdateCostCenterCommandHandler : IRequestHandler<UpdateCostCenterCommand, CostCenterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateCostCenterCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<CostCenterDto> Handle(UpdateCostCenterCommand r, CancellationToken ct)
    {
        var costCenter = await _db.CostCenters.FirstOrDefaultAsync(c => c.Id == r.Id, ct)
            ?? throw new NotFoundException("CostCenter", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            costCenter.SetNames(r.NameAr, r.NameEn);
        if (r.Notes is not null) costCenter.SetNotes(r.Notes);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) costCenter.Activate(_currentUser.UserName);
            else costCenter.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(costCenter);
    }
}

// ------------------------------------------------------------------- HR shifts

public record CreateHrShiftCommand(string Code, int StartMinutes, int EndMinutes, string? NameAr = null,
    string? NameEn = null, int BreakMinutes = 0, int GraceMinutes = 0, string? WorkingDays = null)
    : IRequest<HrShiftDto>;

public class CreateHrShiftCommandValidator : AbstractValidator<CreateHrShiftCommand>
{
    public CreateHrShiftCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.StartMinutes).InclusiveBetween(0, 1439);
        RuleFor(x => x.EndMinutes).InclusiveBetween(0, 1439);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A shift name is required (Arabic and/or English).");
    }
}

public class CreateHrShiftCommandHandler : IRequestHandler<CreateHrShiftCommand, HrShiftDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateHrShiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<HrShiftDto> Handle(CreateHrShiftCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        if (await _db.HrShifts.AnyAsync(s => s.Code == code, ct))
            throw new DuplicateCodeException("HrShift", code);

        var shift = new HrShift(code, r.StartMinutes, r.EndMinutes, _currentUser.UserName,
            r.NameAr, r.NameEn, r.BreakMinutes, r.GraceMinutes, r.WorkingDays);
        _db.HrShifts.Add(shift);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(shift);
    }
}

public record UpdateHrShiftCommand(Guid Id, string? NameAr = null, string? NameEn = null, int? StartMinutes = null,
    int? EndMinutes = null, int? BreakMinutes = null, int? GraceMinutes = null, string? WorkingDays = null,
    bool? IsActive = null) : IRequest<HrShiftDto>;

public class UpdateHrShiftCommandHandler : IRequestHandler<UpdateHrShiftCommand, HrShiftDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateHrShiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<HrShiftDto> Handle(UpdateHrShiftCommand r, CancellationToken ct)
    {
        var shift = await _db.HrShifts.FirstOrDefaultAsync(s => s.Id == r.Id, ct)
            ?? throw new NotFoundException("HrShift", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            shift.SetNames(r.NameAr, r.NameEn);
        if (r.StartMinutes.HasValue || r.EndMinutes.HasValue || r.BreakMinutes.HasValue || r.GraceMinutes.HasValue)
            shift.SetTiming(r.StartMinutes ?? shift.StartMinutes, r.EndMinutes ?? shift.EndMinutes,
                r.BreakMinutes ?? shift.BreakMinutes, r.GraceMinutes ?? shift.GraceMinutes);
        if (r.WorkingDays is not null) shift.SetWorkingDays(r.WorkingDays);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) shift.Activate(_currentUser.UserName);
            else shift.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(shift);
    }
}

// --------------------------------------------------------- attendance policies

public record CreateAttendanceRuleCommand(string Name, decimal StandardDailyHours = 8m,
    decimal StandardWorkingDaysPerMonth = 26m, int GraceMinutes = 0, decimal LatePenaltyPerMinute = 0m,
    decimal EarlyLeavePenaltyPerMinute = 0m, decimal AbsenceDayDeductionFactor = 1m, int OvertimeGraceMinutes = 0,
    decimal OvertimeNormalMultiplier = 1.5m, decimal OvertimeWeekendMultiplier = 2m,
    decimal OvertimeHolidayMultiplier = 2.5m, string? WeekendDays = "5,6",
    bool PaidLeaveCountsAsAttendance = true, bool OvertimeRequiresApproval = true, bool Activate = true)
    : IRequest<AttendanceRuleDto>;

public class CreateAttendanceRuleCommandValidator : AbstractValidator<CreateAttendanceRuleCommand>
{
    public CreateAttendanceRuleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.StandardDailyHours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(x => x.StandardWorkingDaysPerMonth).GreaterThan(0).LessThanOrEqualTo(31);
        RuleFor(x => x.OvertimeNormalMultiplier).GreaterThan(0);
        RuleFor(x => x.OvertimeWeekendMultiplier).GreaterThan(0);
        RuleFor(x => x.OvertimeHolidayMultiplier).GreaterThan(0);
    }
}

public class CreateAttendanceRuleCommandHandler : IRequestHandler<CreateAttendanceRuleCommand, AttendanceRuleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateAttendanceRuleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<AttendanceRuleDto> Handle(CreateAttendanceRuleCommand r, CancellationToken ct)
    {
        var rule = new AttendanceRule(r.Name, _currentUser.UserName);
        rule.Configure(r.StandardDailyHours, r.StandardWorkingDaysPerMonth, r.GraceMinutes,
            r.LatePenaltyPerMinute, r.EarlyLeavePenaltyPerMinute, r.AbsenceDayDeductionFactor,
            r.OvertimeGraceMinutes, r.OvertimeNormalMultiplier, r.OvertimeWeekendMultiplier, r.OvertimeHolidayMultiplier,
            r.WeekendDays, r.PaidLeaveCountsAsAttendance, r.OvertimeRequiresApproval);

        // Only ONE policy is active at a time - the calculator must never have to choose.
        if (r.Activate)
        {
            var existing = await _db.AttendanceRules.Where(a => a.IsActive).ToListAsync(ct);
            foreach (var other in existing) other.Deactivate(_currentUser.UserName);
        }
        else
        {
            rule.Deactivate(_currentUser.UserName);
        }

        _db.AttendanceRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(rule);
    }
}

public record UpdateAttendanceRuleCommand(Guid Id, string? Name = null, decimal? StandardDailyHours = null,
    decimal? StandardWorkingDaysPerMonth = null, int? GraceMinutes = null, decimal? LatePenaltyPerMinute = null,
    decimal? EarlyLeavePenaltyPerMinute = null, decimal? AbsenceDayDeductionFactor = null,
    int? OvertimeGraceMinutes = null, decimal? OvertimeNormalMultiplier = null,
    decimal? OvertimeWeekendMultiplier = null, decimal? OvertimeHolidayMultiplier = null, string? WeekendDays = null,
    bool? PaidLeaveCountsAsAttendance = null, bool? OvertimeRequiresApproval = null, bool? IsActive = null)
    : IRequest<AttendanceRuleDto>;

public class UpdateAttendanceRuleCommandHandler : IRequestHandler<UpdateAttendanceRuleCommand, AttendanceRuleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateAttendanceRuleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<AttendanceRuleDto> Handle(UpdateAttendanceRuleCommand r, CancellationToken ct)
    {
        var rule = await _db.AttendanceRules.FirstOrDefaultAsync(a => a.Id == r.Id, ct)
            ?? throw new NotFoundException("AttendanceRule", r.Id);

        if (!string.IsNullOrWhiteSpace(r.Name)) rule.SetName(r.Name);

        rule.Configure(
            r.StandardDailyHours ?? rule.StandardDailyHours,
            r.StandardWorkingDaysPerMonth ?? rule.StandardWorkingDaysPerMonth,
            r.GraceMinutes ?? rule.GraceMinutes,
            r.LatePenaltyPerMinute ?? rule.LatePenaltyPerMinute,
            r.EarlyLeavePenaltyPerMinute ?? rule.EarlyLeavePenaltyPerMinute,
            r.AbsenceDayDeductionFactor ?? rule.AbsenceDayDeductionFactor,
            r.OvertimeGraceMinutes ?? rule.OvertimeGraceMinutes,
            r.OvertimeNormalMultiplier ?? rule.OvertimeNormalMultiplier,
            r.OvertimeWeekendMultiplier ?? rule.OvertimeWeekendMultiplier,
            r.OvertimeHolidayMultiplier ?? rule.OvertimeHolidayMultiplier,
            r.WeekendDays ?? rule.WeekendDays,
            r.PaidLeaveCountsAsAttendance ?? rule.PaidLeaveCountsAsAttendance,
            r.OvertimeRequiresApproval ?? rule.OvertimeRequiresApproval);

        if (r.IsActive == true)
        {
            var others = await _db.AttendanceRules.Where(a => a.IsActive && a.Id != rule.Id).ToListAsync(ct);
            foreach (var other in others) other.Deactivate(_currentUser.UserName);
            rule.Activate(_currentUser.UserName);
        }
        else if (r.IsActive == false)
        {
            rule.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(rule);
    }
}

// ----------------------------------------------------------------- leave types

public record CreateLeaveTypeCommand(string Code, string? NameAr = null, string? NameEn = null, bool IsPaid = true,
    decimal AnnualEntitlementDays = 0m, bool RequiresApproval = true) : IRequest<LeaveTypeDto>;

public class CreateLeaveTypeCommandValidator : AbstractValidator<CreateLeaveTypeCommand>
{
    public CreateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.AnnualEntitlementDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A leave type name is required (Arabic and/or English).");
    }
}

public class CreateLeaveTypeCommandHandler : IRequestHandler<CreateLeaveTypeCommand, LeaveTypeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateLeaveTypeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<LeaveTypeDto> Handle(CreateLeaveTypeCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        if (await _db.LeaveTypes.AnyAsync(t => t.Code == code, ct))
            throw new DuplicateCodeException("LeaveType", code);

        var type = new LeaveType(code, _currentUser.UserName, r.NameAr, r.NameEn, r.IsPaid,
            r.AnnualEntitlementDays, r.RequiresApproval);
        _db.LeaveTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(type);
    }
}

public record UpdateLeaveTypeCommand(Guid Id, string? NameAr = null, string? NameEn = null, bool? IsPaid = null,
    decimal? AnnualEntitlementDays = null, bool? RequiresApproval = null, bool? IsActive = null)
    : IRequest<LeaveTypeDto>;

public class UpdateLeaveTypeCommandHandler : IRequestHandler<UpdateLeaveTypeCommand, LeaveTypeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateLeaveTypeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<LeaveTypeDto> Handle(UpdateLeaveTypeCommand r, CancellationToken ct)
    {
        var type = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == r.Id, ct)
            ?? throw new NotFoundException("LeaveType", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            type.SetNames(r.NameAr, r.NameEn);
        type.Configure(r.IsPaid ?? type.IsPaid, r.AnnualEntitlementDays ?? type.AnnualEntitlementDays,
            r.RequiresApproval ?? type.RequiresApproval);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) type.Activate(_currentUser.UserName);
            else type.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(type);
    }
}

// -------------------------------------------------------------- pay components

public record CreatePayComponentCommand(string Code, PayComponentKind Kind, PayComponentCalculation Calculation,
    string? NameAr = null, string? NameEn = null, decimal DefaultAmount = 0m, decimal DefaultPercent = 0m,
    bool IsTaxable = true, bool IsInsured = true, int SortOrder = 0, string? Notes = null)
    : IRequest<PayComponentDto>;

public class CreatePayComponentCommandValidator : AbstractValidator<CreatePayComponentCommand>
{
    public CreatePayComponentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Calculation).IsInEnum();
        RuleFor(x => x.DefaultAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultPercent).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A pay component name is required (Arabic and/or English).");
    }
}

public class CreatePayComponentCommandHandler : IRequestHandler<CreatePayComponentCommand, PayComponentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreatePayComponentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<PayComponentDto> Handle(CreatePayComponentCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        if (await _db.PayComponents.AnyAsync(c => c.Code == code, ct))
            throw new DuplicateCodeException("PayComponent", code);

        var component = new PayComponent(code, r.Kind, r.Calculation, _currentUser.UserName, r.NameAr, r.NameEn,
            r.DefaultAmount, r.DefaultPercent, r.IsTaxable, r.IsInsured, r.SortOrder, r.Notes);
        _db.PayComponents.Add(component);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(component);
    }
}

public record UpdatePayComponentCommand(Guid Id, string? NameAr = null, string? NameEn = null,
    PayComponentKind? Kind = null, PayComponentCalculation? Calculation = null, decimal? DefaultAmount = null,
    decimal? DefaultPercent = null, bool? IsTaxable = null, bool? IsInsured = null, int? SortOrder = null,
    string? Notes = null, bool? IsActive = null) : IRequest<PayComponentDto>;

public class UpdatePayComponentCommandHandler : IRequestHandler<UpdatePayComponentCommand, PayComponentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdatePayComponentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<PayComponentDto> Handle(UpdatePayComponentCommand r, CancellationToken ct)
    {
        var component = await _db.PayComponents.FirstOrDefaultAsync(c => c.Id == r.Id, ct)
            ?? throw new NotFoundException("PayComponent", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            component.SetNames(r.NameAr, r.NameEn);

        component.Configure(r.Kind ?? component.Kind, r.Calculation ?? component.Calculation,
            r.DefaultAmount ?? component.DefaultAmount, r.DefaultPercent ?? component.DefaultPercent,
            r.IsTaxable ?? component.IsTaxable, r.IsInsured ?? component.IsInsured, r.SortOrder ?? component.SortOrder);

        if (r.Notes is not null) component.SetNotes(r.Notes);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) component.Activate(_currentUser.UserName);
            else component.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(component);
    }
}

// ------------------------------------------------------- contribution rules

public record CreateContributionRuleCommand(string Code, ContributionKind Kind, ContributionBase Base,
    decimal EmployeeRatePercent, decimal EmployerRatePercent, string? NameAr = null, string? NameEn = null,
    decimal? MinBase = null, decimal? MaxBase = null) : IRequest<ContributionRuleDto>;

public class CreateContributionRuleCommandValidator : AbstractValidator<CreateContributionRuleCommand>
{
    public CreateContributionRuleCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Base).IsInEnum();
        RuleFor(x => x.EmployeeRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.EmployerRatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A contribution name is required (Arabic and/or English).");
    }
}

public class CreateContributionRuleCommandHandler : IRequestHandler<CreateContributionRuleCommand, ContributionRuleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateContributionRuleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<ContributionRuleDto> Handle(CreateContributionRuleCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        if (await _db.ContributionRules.AnyAsync(c => c.Code == code, ct))
            throw new DuplicateCodeException("ContributionRule", code);

        var rule = new ContributionRule(code, r.Kind, r.Base, r.EmployeeRatePercent, r.EmployerRatePercent,
            _currentUser.UserName, r.NameAr, r.NameEn, r.MinBase, r.MaxBase);
        _db.ContributionRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(rule);
    }
}

public record UpdateContributionRuleCommand(Guid Id, string? NameAr = null, string? NameEn = null,
    ContributionKind? Kind = null, ContributionBase? Base = null, decimal? EmployeeRatePercent = null,
    decimal? EmployerRatePercent = null, decimal? MinBase = null, decimal? MaxBase = null, bool? IsActive = null)
    : IRequest<ContributionRuleDto>;

public class UpdateContributionRuleCommandHandler : IRequestHandler<UpdateContributionRuleCommand, ContributionRuleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateContributionRuleCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<ContributionRuleDto> Handle(UpdateContributionRuleCommand r, CancellationToken ct)
    {
        var rule = await _db.ContributionRules.FirstOrDefaultAsync(c => c.Id == r.Id, ct)
            ?? throw new NotFoundException("ContributionRule", r.Id);

        if (!string.IsNullOrWhiteSpace(r.NameAr) || !string.IsNullOrWhiteSpace(r.NameEn))
            rule.SetNames(r.NameAr, r.NameEn);
        rule.Configure(r.Kind ?? rule.Kind, r.Base ?? rule.Base,
            r.EmployeeRatePercent ?? rule.EmployeeRatePercent, r.EmployerRatePercent ?? rule.EmployerRatePercent,
            r.MinBase ?? rule.MinBase, r.MaxBase ?? rule.MaxBase);
        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) rule.Activate(_currentUser.UserName);
            else rule.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(rule);
    }
}

// --------------------------------------------------------- accounting mappings

/// <summary>
/// Creates or replaces the mapping for one payroll amount kind (spec section 37).
/// Unique per kind, so the export can never find two accounts for the same amount.
/// </summary>
public record UpsertPayrollAccountMappingCommand(PayrollAccountKind Kind, string AccountCode, string AccountName,
    string? Notes = null, bool IsActive = true) : IRequest<PayrollAccountMappingDto>;

public class UpsertPayrollAccountMappingCommandValidator : AbstractValidator<UpsertPayrollAccountMappingCommand>
{
    public UpsertPayrollAccountMappingCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.AccountCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(200);
    }
}

public class UpsertPayrollAccountMappingCommandHandler
    : IRequestHandler<UpsertPayrollAccountMappingCommand, PayrollAccountMappingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpsertPayrollAccountMappingCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<PayrollAccountMappingDto> Handle(UpsertPayrollAccountMappingCommand r, CancellationToken ct)
    {
        var mapping = await _db.PayrollAccountMappings.FirstOrDefaultAsync(m => m.Kind == r.Kind, ct);

        if (mapping is null)
        {
            mapping = new PayrollAccountMapping(r.Kind, r.AccountCode, r.AccountName, _currentUser.UserName, r.Notes);
            _db.PayrollAccountMappings.Add(mapping);
        }
        else
        {
            mapping.SetAccount(r.AccountCode, r.AccountName);
            if (r.Notes is not null) mapping.SetNotes(r.Notes);
            if (r.IsActive) mapping.Activate(_currentUser.UserName);
            else mapping.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);
        return HrMapping.Map(mapping);
    }
}

// --------------------------------------------------- employee pay components

public record AssignEmployeePayComponentCommand(Guid EmployeeId, Guid PayComponentId, DateTime EffectiveFrom,
    decimal? Amount = null, decimal? Percent = null, DateTime? EffectiveTo = null) : IRequest<EmployeePayComponentDto>;

public class AssignEmployeePayComponentCommandValidator : AbstractValidator<AssignEmployeePayComponentCommand>
{
    public AssignEmployeePayComponentCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.PayComponentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.Percent).InclusiveBetween(0, 1000).When(x => x.Percent.HasValue);
    }
}

public class AssignEmployeePayComponentCommandHandler
    : IRequestHandler<AssignEmployeePayComponentCommand, EmployeePayComponentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public AssignEmployeePayComponentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<EmployeePayComponentDto> Handle(AssignEmployeePayComponentCommand r, CancellationToken ct)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", r.EmployeeId);
        var component = await _db.PayComponents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.PayComponentId, ct)
            ?? throw new NotFoundException("PayComponent", r.PayComponentId);

        var assignment = new EmployeePayComponent(r.EmployeeId, r.PayComponentId, r.EffectiveFrom,
            _currentUser.UserName, r.Amount, r.Percent, r.EffectiveTo);
        _db.EmployeePayComponents.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return new EmployeePayComponentDto
        {
            Id = assignment.Id, EmployeeId = assignment.EmployeeId, EmployeeCode = employee.Code,
            EmployeeName = employee.Name, PayComponentId = assignment.PayComponentId,
            PayComponentCode = component.Code, PayComponentName = component.Name,
            Amount = assignment.Amount, Percent = assignment.Percent,
            EffectiveFrom = assignment.EffectiveFrom, EffectiveTo = assignment.EffectiveTo, IsActive = assignment.IsActive
        };
    }
}

public record UpdateEmployeePayComponentCommand(Guid Id, decimal? Amount = null, bool ClearAmount = false,
    decimal? Percent = null, bool ClearPercent = false, DateTime? EffectiveFrom = null, DateTime? EffectiveTo = null,
    bool ClearEffectiveTo = false, bool? IsActive = null) : IRequest<EmployeePayComponentDto>;

public class UpdateEmployeePayComponentCommandHandler
    : IRequestHandler<UpdateEmployeePayComponentCommand, EmployeePayComponentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateEmployeePayComponentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<EmployeePayComponentDto> Handle(UpdateEmployeePayComponentCommand r, CancellationToken ct)
    {
        var assignment = await _db.EmployeePayComponents.FirstOrDefaultAsync(a => a.Id == r.Id, ct)
            ?? throw new NotFoundException("EmployeePayComponent", r.Id);

        assignment.Update(
            r.ClearAmount ? null : r.Amount ?? assignment.Amount,
            r.ClearPercent ? null : r.Percent ?? assignment.Percent,
            r.EffectiveFrom ?? assignment.EffectiveFrom,
            r.ClearEffectiveTo ? null : r.EffectiveTo ?? assignment.EffectiveTo,
            _currentUser.UserName);

        if (r.IsActive.HasValue)
        {
            if (r.IsActive.Value) assignment.Activate(_currentUser.UserName);
            else assignment.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(ct);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == assignment.EmployeeId, ct);
        var component = await _db.PayComponents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == assignment.PayComponentId, ct);

        return new EmployeePayComponentDto
        {
            Id = assignment.Id, EmployeeId = assignment.EmployeeId, EmployeeCode = employee?.Code ?? "",
            EmployeeName = employee?.Name ?? "", PayComponentId = assignment.PayComponentId,
            PayComponentCode = component?.Code ?? "", PayComponentName = component?.Name ?? "",
            Amount = assignment.Amount, Percent = assignment.Percent,
            EffectiveFrom = assignment.EffectiveFrom, EffectiveTo = assignment.EffectiveTo, IsActive = assignment.IsActive
        };
    }
}

// ------------------------------------------------- employee HR profile (sections 28-36)

/// <summary>
/// Sets an employee's HR classification (section, job, cost center, attendance
/// shift) and their statutory exemption flags. All of it is optional configuration:
/// a factory that does not use sections simply leaves them empty.
/// </summary>
public record SetEmployeeHrProfileCommand(Guid EmployeeId, Guid? HrSectionId = null, Guid? JobPositionId = null,
    Guid? CostCenterId = null, Guid? HrShiftId = null, bool? IsTaxExempt = null, bool? IsInsuranceExempt = null)
    : IRequest<EmployeeDto>;

public class SetEmployeeHrProfileCommandHandler : IRequestHandler<SetEmployeeHrProfileCommand, EmployeeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetEmployeeHrProfileCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<EmployeeDto> Handle(SetEmployeeHrProfileCommand r, CancellationToken ct)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", r.EmployeeId);

        if (r.HrSectionId.HasValue && !await _db.HrSections.AnyAsync(s => s.Id == r.HrSectionId.Value, ct))
            throw new NotFoundException("HrSection", r.HrSectionId.Value);
        if (r.JobPositionId.HasValue && !await _db.JobPositions.AnyAsync(j => j.Id == r.JobPositionId.Value, ct))
            throw new NotFoundException("JobPosition", r.JobPositionId.Value);
        if (r.CostCenterId.HasValue && !await _db.CostCenters.AnyAsync(c => c.Id == r.CostCenterId.Value, ct))
            throw new NotFoundException("CostCenter", r.CostCenterId.Value);
        if (r.HrShiftId.HasValue && !await _db.HrShifts.AnyAsync(s => s.Id == r.HrShiftId.Value, ct))
            throw new NotFoundException("HrShift", r.HrShiftId.Value);

        employee.SetHrClassification(r.HrSectionId, r.JobPositionId, r.CostCenterId, r.HrShiftId, _currentUser.UserName);

        if (r.IsTaxExempt.HasValue || r.IsInsuranceExempt.HasValue)
            employee.SetTaxInsuranceExemption(
                r.IsTaxExempt ?? employee.IsTaxExempt,
                r.IsInsuranceExempt ?? employee.IsInsuranceExempt,
                _currentUser.UserName);

        await _db.SaveChangesAsync(ct);

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.Id == employee.DepartmentId)
            .Select(d => new { d.Code, d.Name })
            .FirstOrDefaultAsync(ct);

        return new EmployeeDto
        {
            Id = employee.Id, Code = employee.Code, Name = employee.Name, NameAr = employee.NameAr,
            NameEn = employee.NameEn, DepartmentId = employee.DepartmentId,
            DepartmentCode = departments?.Code ?? "", DepartmentName = departments?.Name ?? "",
            JobTitle = employee.JobTitle, BasicSalary = employee.BasicSalary, Status = employee.Status,
            HireDate = employee.HireDate, TerminationDate = employee.TerminationDate, Phone = employee.Phone,
            NationalId = employee.NationalId, BankAccountNumber = employee.BankAccountNumber, Notes = employee.Notes,
            HrSectionId = employee.HrSectionId, JobPositionId = employee.JobPositionId,
            CostCenterId = employee.CostCenterId, HrShiftId = employee.HrShiftId,
            IsTaxExempt = employee.IsTaxExempt, IsInsuranceExempt = employee.IsInsuranceExempt
        };
    }
}

public record RemoveEmployeePayComponentCommand(Guid Id) : IRequest<Unit>;

public class RemoveEmployeePayComponentCommandHandler : IRequestHandler<RemoveEmployeePayComponentCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RemoveEmployeePayComponentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<Unit> Handle(RemoveEmployeePayComponentCommand r, CancellationToken ct)
    {
        var assignment = await _db.EmployeePayComponents.FirstOrDefaultAsync(a => a.Id == r.Id, ct)
            ?? throw new NotFoundException("EmployeePayComponent", r.Id);

        // Never delete a historic assignment if it was already used: deactivate instead.
        assignment.Deactivate(_currentUser.UserName);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
