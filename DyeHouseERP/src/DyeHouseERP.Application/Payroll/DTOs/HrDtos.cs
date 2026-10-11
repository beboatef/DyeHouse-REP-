using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Payroll.DTOs;

// --------------------------------------------------------------------------- HR masters

public class HrSectionDto
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class JobPositionDto
{
    public Guid Id { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public decimal? DefaultBasicSalary { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class CostCenterDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class HrShiftDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>Minutes from midnight, kept numeric so the UI can render any locale's clock.</summary>
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int GraceMinutes { get; set; }
    public string WorkingDays { get; set; } = string.Empty;
    public bool IsNightShift { get; set; }
    public bool IsActive { get; set; }
    public int PlannedMinutes { get; set; }
}

public class AttendanceRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal StandardDailyHours { get; set; }
    public decimal StandardWorkingDaysPerMonth { get; set; }
    public int GraceMinutes { get; set; }
    public decimal LatePenaltyPerMinute { get; set; }
    public decimal EarlyLeavePenaltyPerMinute { get; set; }
    public decimal AbsenceDayDeductionFactor { get; set; }
    public int OvertimeGraceMinutes { get; set; }
    public decimal OvertimeNormalMultiplier { get; set; }
    public decimal OvertimeWeekendMultiplier { get; set; }
    public decimal OvertimeHolidayMultiplier { get; set; }
    public string WeekendDays { get; set; } = string.Empty;
    public bool PaidLeaveCountsAsAttendance { get; set; }
    public bool OvertimeRequiresApproval { get; set; }
    public bool IsActive { get; set; }
}

public class LeaveTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public decimal AnnualEntitlementDays { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
}

public class PayComponentDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public PayComponentKind Kind { get; set; }
    public PayComponentCalculation Calculation { get; set; }
    public decimal DefaultAmount { get; set; }
    public decimal DefaultPercent { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsInsured { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class EmployeePayComponentDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid PayComponentId { get; set; }
    public string PayComponentCode { get; set; } = string.Empty;
    public string PayComponentName { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public decimal? Percent { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
}

public class ContributionRuleDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ContributionKind Kind { get; set; }
    public ContributionBase Base { get; set; }
    public decimal EmployeeRatePercent { get; set; }
    public decimal EmployerRatePercent { get; set; }
    public decimal? MinBase { get; set; }
    public decimal? MaxBase { get; set; }
    public bool IsActive { get; set; }
}

public class PayrollAccountMappingDto
{
    public Guid Id { get; set; }
    public PayrollAccountKind Kind { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

// --------------------------------------------------------------------- attendance

public class AttendanceEventDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public Guid? HrShiftId { get; set; }
    public string? ShiftName { get; set; }
    public int? CheckInMinutes { get; set; }
    public int? CheckOutMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public AttendanceStatus Status { get; set; }
    public AttendanceSource Source { get; set; }
    public Guid? LeaveRequestId { get; set; }
    public decimal AbsenceDays { get; set; }
    public string? Notes { get; set; }
}

public class AttendanceSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int PresentDays { get; set; }
    public int LateDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int LateMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public decimal WorkedHours { get; set; }
}

// ------------------------------------------------------------ leave / overtime / advances

public class LeaveRequestDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveStatus Status { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public bool AttendanceApplied { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class OvertimeEntryDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public OvertimeCategory Category { get; set; }
    public string Reason { get; set; } = string.Empty;
    public OvertimeStatus Status { get; set; }
    public decimal ApprovedMultiplier { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? PayrollRunId { get; set; }
}

public class EmployeeAdvanceDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime AdvanceDate { get; set; }
    public decimal Amount { get; set; }
    public int InstalmentCount { get; set; }
    public decimal InstalmentAmount { get; set; }
    public decimal RecoveredAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public AdvanceStatus Status { get; set; }
}

// ------------------------------------------------------------------- reporting

/// <summary>Labor cost grouped by department / section / cost center (spec section 40).</summary>
public class LaborCostRowDto
{
    public string GroupKey { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public int EmployeeCount { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal Overtime { get; set; }
    public decimal Deductions { get; set; }
    public decimal Tax { get; set; }
    public decimal Insurance { get; set; }
    public decimal EmployerInsurance { get; set; }
    public decimal Advances { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
    /// <summary>Gross + employer contributions: the true cost to the factory.</summary>
    public decimal EmployerCost { get; set; }
}

public class LaborCostReportDto
{
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public List<LaborCostRowDto> Rows { get; set; } = new();
    public decimal TotalBasicSalary { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalEmployerCost { get; set; }
}

/// <summary>A payroll amount mapped to its configured accounting account (spec section 37).</summary>
public class PayrollAccountingLineDto
{
    public PayrollAccountKind Kind { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class PayrollAccountingExportDto
{
    public Guid RunId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public List<PayrollAccountingLineDto> Lines { get; set; } = new();
    public List<string> UnmappedKinds { get; set; } = new();
}
