using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// HR, attendance and the configurable wage rules (spec sections 28-42).
///
/// Everything the payroll calculation uses is maintained HERE as master data:
/// sections, jobs, cost centers, HR attendance shifts (deliberately separate from
/// the production execution shifts), the attendance policy, leave types, pay
/// components, statutory contribution rules and the accounting mappings. Nothing
/// factory-specific is hard-coded in the calculation, so a factory configures its
/// own rules and the payroll follows them.
///
/// Leave and overtime are requested by one user and DECIDED by a different,
/// authenticated one: the requester can never approve their own request.
/// </summary>
[ApiController]
[Route("api/hr")]
[Authorize]
public class HrController : ControllerBase
{
    private readonly ISender _mediator;
    public HrController(ISender mediator) => _mediator = mediator;

    // ----------------------------------------------------------------- sections

    [HttpGet("sections")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<HrSectionDto>>> GetSections([FromQuery] Guid? departmentId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetHrSectionsQuery(departmentId, activeOnly)));

    [HttpPost("sections")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrSectionDto>> CreateSection([FromBody] CreateHrSectionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSections), new { }, result);
    }

    [HttpPut("sections/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrSectionDto>> UpdateSection(Guid id, [FromBody] UpdateHrSectionCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------- job positions

    [HttpGet("jobs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<JobPositionDto>>> GetJobs([FromQuery] Guid? departmentId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetJobPositionsQuery(departmentId, activeOnly)));

    [HttpPost("jobs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<JobPositionDto>> CreateJob([FromBody] CreateJobPositionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetJobs), new { }, result);
    }

    [HttpPut("jobs/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<JobPositionDto>> UpdateJob(Guid id, [FromBody] UpdateJobPositionCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------- cost centers

    [HttpGet("cost-centers")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<CostCenterDto>>> GetCostCenters([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetCostCentersQuery(activeOnly)));

    [HttpPost("cost-centers")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<CostCenterDto>> CreateCostCenter([FromBody] CreateCostCenterCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetCostCenters), new { }, result);
    }

    [HttpPut("cost-centers/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<CostCenterDto>> UpdateCostCenter(Guid id, [FromBody] UpdateCostCenterCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ---------------------------------------------------------------- HR shifts

    [HttpGet("shifts")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<HrShiftDto>>> GetShifts([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetHrShiftsQuery(activeOnly)));

    [HttpPost("shifts")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrShiftDto>> CreateShift([FromBody] CreateHrShiftCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetShifts), new { }, result);
    }

    [HttpPut("shifts/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrShiftDto>> UpdateShift(Guid id, [FromBody] UpdateHrShiftCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------ attendance policies

    [HttpGet("attendance-rules")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceRuleDto>>> GetAttendanceRules([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetAttendanceRulesQuery(activeOnly)));

    [HttpPost("attendance-rules")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<AttendanceRuleDto>> CreateAttendanceRule([FromBody] CreateAttendanceRuleCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAttendanceRules), new { }, result);
    }

    [HttpPut("attendance-rules/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<AttendanceRuleDto>> UpdateAttendanceRule(Guid id, [FromBody] UpdateAttendanceRuleCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // --------------------------------------------------------------- leave types

    [HttpGet("leave-types")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<LeaveTypeDto>>> GetLeaveTypes([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetLeaveTypesQuery(activeOnly)));

    [HttpPost("leave-types")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<LeaveTypeDto>> CreateLeaveType([FromBody] CreateLeaveTypeCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetLeaveTypes), new { }, result);
    }

    [HttpPut("leave-types/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<LeaveTypeDto>> UpdateLeaveType(Guid id, [FromBody] UpdateLeaveTypeCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------ pay components

    [HttpGet("pay-components")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<PayComponentDto>>> GetPayComponents([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetPayComponentsQuery(activeOnly)));

    [HttpPost("pay-components")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<PayComponentDto>> CreatePayComponent([FromBody] CreatePayComponentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetPayComponents), new { }, result);
    }

    [HttpPut("pay-components/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<PayComponentDto>> UpdatePayComponent(Guid id, [FromBody] UpdatePayComponentCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------- employee pay components

    [HttpGet("employee-pay-components")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<EmployeePayComponentDto>>> GetEmployeePayComponents(
        [FromQuery] Guid? employeeId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetEmployeePayComponentsQuery(employeeId, activeOnly)));

    [HttpPost("employee-pay-components")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<EmployeePayComponentDto>> AssignEmployeePayComponent(
        [FromBody] AssignEmployeePayComponentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetEmployeePayComponents), new { }, result);
    }

    [HttpPut("employee-pay-components/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<EmployeePayComponentDto>> UpdateEmployeePayComponent(
        Guid id, [FromBody] UpdateEmployeePayComponentCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    [HttpDelete("employee-pay-components/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<IActionResult> RemoveEmployeePayComponent(Guid id)
    {
        await _mediator.Send(new RemoveEmployeePayComponentCommand(id));
        return NoContent();
    }

    // ----------------------------------------------------------- contributions

    [HttpGet("contribution-rules")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<ContributionRuleDto>>> GetContributionRules([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetContributionRulesQuery(activeOnly)));

    [HttpPost("contribution-rules")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<ContributionRuleDto>> CreateContributionRule(
        [FromBody] CreateContributionRuleCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetContributionRules), new { }, result);
    }

    [HttpPut("contribution-rules/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<ContributionRuleDto>> UpdateContributionRule(
        Guid id, [FromBody] UpdateContributionRuleCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------ accounting mappings

    [HttpGet("account-mappings")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollAccounting)]
    public async Task<ActionResult<List<PayrollAccountMappingDto>>> GetAccountMappings()
        => Ok(await _mediator.Send(new GetPayrollAccountMappingsQuery()));

    [HttpPost("account-mappings")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollAccounting)]
    public async Task<ActionResult<PayrollAccountMappingDto>> UpsertAccountMapping(
        [FromBody] UpsertPayrollAccountMappingCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAccountMappings), new { }, result);
    }

    // ------------------------------------------------------- employee HR profile

    [HttpPut("employees/{id:guid}/profile")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<EmployeeDto>> SetEmployeeProfile(Guid id, [FromBody] SetEmployeeHrProfileCommand command)
        => Ok(await _mediator.Send(new SetEmployeeHrProfileCommand(id, command.HrSectionId, command.JobPositionId,
            command.CostCenterId, command.HrShiftId, command.IsTaxExempt, command.IsInsuranceExempt)));

    // -------------------------------------------------------------- attendance

    [HttpGet("attendance")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceEventDto>>> GetAttendance(
        [FromQuery] Guid? employeeId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] AttendanceStatus? status, [FromQuery] Guid? departmentId)
        => Ok(await _mediator.Send(new GetAttendanceQuery(employeeId, fromDate, toDate, status, departmentId)));

    [HttpGet("attendance/summary")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceSummaryDto>>> GetAttendanceSummary(
        [FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] Guid? departmentId)
        => Ok(await _mediator.Send(new GetAttendanceSummaryQuery(fromDate, toDate, departmentId)));

    [HttpPost("attendance")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<AttendanceEventDto>> RecordAttendance([FromBody] RecordAttendanceCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("attendance/non-working")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<AttendanceEventDto>> RecordNonWorkingDay([FromBody] RecordNonWorkingDayCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("attendance/generate")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<object>> GenerateAttendance([FromBody] GenerateAttendanceForPeriodCommand command)
        => Ok(new { created = await _mediator.Send(command) });

    [HttpDelete("attendance/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<IActionResult> RemoveAttendance(Guid id)
    {
        await _mediator.Send(new RemoveAttendanceEventCommand(id));
        return NoContent();
    }

    // ------------------------------------------------------------------- leave

    [HttpGet("leave-requests")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<LeaveRequestDto>>> GetLeaveRequests(
        [FromQuery] LeaveStatus? status, [FromQuery] Guid? employeeId)
        => Ok(await _mediator.Send(new GetLeaveRequestsQuery(status, employeeId)));

    [HttpPost("leave-requests")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveRequest)]
    public async Task<ActionResult<LeaveRequestDto>> SubmitLeaveRequest([FromBody] SubmitLeaveRequestCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("leave-requests/{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveApprove)]
    public async Task<ActionResult<LeaveRequestDto>> ApproveLeaveRequest(Guid id, [FromBody] HrDecisionNote? note)
        => Ok(await _mediator.Send(new ApproveLeaveRequestCommand(id, note?.Note)));

    [HttpPost("leave-requests/{id:guid}/reject")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveApprove)]
    public async Task<ActionResult<LeaveRequestDto>> RejectLeaveRequest(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new RejectLeaveRequestCommand(id, request.Reason ?? string.Empty)));

    [HttpPost("leave-requests/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveRequest)]
    public async Task<ActionResult<LeaveRequestDto>> CancelLeaveRequest(Guid id, [FromBody] HrDecisionNote? note)
        => Ok(await _mediator.Send(new CancelLeaveRequestCommand(id, note?.Note)));

    // ---------------------------------------------------------------- overtime

    [HttpGet("overtime")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<OvertimeEntryDto>>> GetOvertime(
        [FromQuery] OvertimeStatus? status, [FromQuery] Guid? employeeId,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        => Ok(await _mediator.Send(new GetOvertimeEntriesQuery(status, employeeId, fromDate, toDate)));

    [HttpPost("overtime")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeRequest)]
    public async Task<ActionResult<OvertimeEntryDto>> SubmitOvertime([FromBody] SubmitOvertimeCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("overtime/{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeApprove)]
    public async Task<ActionResult<OvertimeEntryDto>> ApproveOvertime(Guid id)
        => Ok(await _mediator.Send(new ApproveOvertimeCommand(id)));

    [HttpPost("overtime/{id:guid}/reject")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeApprove)]
    public async Task<ActionResult<OvertimeEntryDto>> RejectOvertime(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new RejectOvertimeCommand(id, request.Reason ?? string.Empty)));

    // ---------------------------------------------------------------- advances

    [HttpGet("advances")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<EmployeeAdvanceDto>>> GetAdvances(
        [FromQuery] Guid? employeeId, [FromQuery] AdvanceStatus? status)
        => Ok(await _mediator.Send(new GetEmployeeAdvancesQuery(employeeId, status)));

    [HttpPost("advances")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAdvancesManage)]
    public async Task<ActionResult<EmployeeAdvanceDto>> CreateAdvance([FromBody] CreateEmployeeAdvanceCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("advances/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAdvancesManage)]
    public async Task<ActionResult<EmployeeAdvanceDto>> CancelAdvance(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new CancelEmployeeAdvanceCommand(id, request.Reason ?? string.Empty)));

    // --------------------------------------------------------------- reporting

    /// <summary>Labor cost by department or cost center for a payroll period (spec section 40).</summary>
    [HttpGet("labor-cost")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLaborCost)]
    public async Task<ActionResult<LaborCostReportDto>> GetLaborCost(
        [FromQuery] int periodYear, [FromQuery] int periodMonth, [FromQuery] bool groupByCostCenter = false)
        => Ok(await _mediator.Send(new GetLaborCostReportQuery(periodYear, periodMonth, groupByCostCenter)));

    /// <summary>Labor cost as Excel or PDF (spec section 40).</summary>
    [HttpGet("labor-cost/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLaborCost)]
    public async Task<IActionResult> ExportLaborCost(
        [FromQuery] int periodYear, [FromQuery] int periodMonth, [FromQuery] bool groupByCostCenter = false,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var report = await _mediator.Send(new GetLaborCostReportQuery(periodYear, periodMonth, groupByCostCenter));
        var headers = new[] { "Group", "Employees", "Basic", "Allowances", "Overtime", "Deductions", "Tax", "Insurance", "Advances", "Gross", "Net", "Employer cost" };
        var rows = report.Rows.Select(r => new object?[]
        {
            r.CostCenterName ?? r.DepartmentName ?? r.GroupKey, r.EmployeeCount, r.BasicSalary, r.Allowances,
            r.Overtime, r.Deductions, r.Tax, r.Insurance, r.Advances, r.Gross, r.Net, r.EmployerCost
        }).ToList<object?[]>();

        rows.Add(new object?[]
        {
            "TOTAL", report.Rows.Sum(r => r.EmployeeCount), report.TotalBasicSalary, null, null, null, null, null,
            null, report.TotalGross, report.TotalNet, report.TotalEmployerCost
        });

        return ExportFileHelper.ToFile(export, format, "Labor cost",
            $"Payroll {periodMonth:00}/{periodYear}", headers, rows, $"labor-cost-{periodYear}{periodMonth:00}", "Labor cost");
    }
}

/// <summary>Body for a leave/overtime/advance decision that carries an optional note.</summary>
public record HrDecisionNote(string? Note = null);

/// <summary>Body for a rejection or cancellation - the reason is mandatory server-side.</summary>
public record HrDecisionRequest(string? Reason = null);
