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
    /// <summary>Initializes a new instance of the HrController class.</summary>
    public HrController(ISender mediator) => _mediator = mediator;

    // ----------------------------------------------------------------- sections

    /// <summary>Gets sections.</summary>
    /// <remarks>Route: GET api/hr/sections. Requires the 'hr.view' permission.</remarks>
    [HttpGet("sections")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<HrSectionDto>>> GetSections([FromQuery] Guid? departmentId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetHrSectionsQuery(departmentId, activeOnly)));

    /// <summary>Creates section.</summary>
    /// <remarks>Route: POST api/hr/sections. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("sections")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrSectionDto>> CreateSection([FromBody] CreateHrSectionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSections), new { }, result);
    }

    /// <summary>Updates section.</summary>
    /// <remarks>Route: PUT api/hr/sections/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("sections/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrSectionDto>> UpdateSection(Guid id, [FromBody] UpdateHrSectionCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------- job positions

    /// <summary>Gets jobs.</summary>
    /// <remarks>Route: GET api/hr/jobs. Requires the 'hr.view' permission.</remarks>
    [HttpGet("jobs")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<JobPositionDto>>> GetJobs([FromQuery] Guid? departmentId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetJobPositionsQuery(departmentId, activeOnly)));

    /// <summary>Creates job.</summary>
    /// <remarks>Route: POST api/hr/jobs. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("jobs")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<JobPositionDto>> CreateJob([FromBody] CreateJobPositionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetJobs), new { }, result);
    }

    /// <summary>Updates job.</summary>
    /// <remarks>Route: PUT api/hr/jobs/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("jobs/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<JobPositionDto>> UpdateJob(Guid id, [FromBody] UpdateJobPositionCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------- cost centers

    /// <summary>Gets cost centers.</summary>
    /// <remarks>Route: GET api/hr/cost-centers. Requires the 'hr.view' permission.</remarks>
    [HttpGet("cost-centers")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<CostCenterDto>>> GetCostCenters([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetCostCentersQuery(activeOnly)));

    /// <summary>Creates cost center.</summary>
    /// <remarks>Route: POST api/hr/cost-centers. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("cost-centers")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<CostCenterDto>> CreateCostCenter([FromBody] CreateCostCenterCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetCostCenters), new { }, result);
    }

    /// <summary>Updates cost center.</summary>
    /// <remarks>Route: PUT api/hr/cost-centers/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("cost-centers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<CostCenterDto>> UpdateCostCenter(Guid id, [FromBody] UpdateCostCenterCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ---------------------------------------------------------------- HR shifts

    /// <summary>Gets shifts.</summary>
    /// <remarks>Route: GET api/hr/shifts. Requires the 'hr.view' permission.</remarks>
    [HttpGet("shifts")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<HrShiftDto>>> GetShifts([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetHrShiftsQuery(activeOnly)));

    /// <summary>Creates shift.</summary>
    /// <remarks>Route: POST api/hr/shifts. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("shifts")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrShiftDto>> CreateShift([FromBody] CreateHrShiftCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetShifts), new { }, result);
    }

    /// <summary>Updates shift.</summary>
    /// <remarks>Route: PUT api/hr/shifts/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("shifts/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<HrShiftDto>> UpdateShift(Guid id, [FromBody] UpdateHrShiftCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------ attendance policies

    /// <summary>Gets attendance rules.</summary>
    /// <remarks>Route: GET api/hr/attendance-rules. Requires the 'hr.view' permission.</remarks>
    [HttpGet("attendance-rules")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceRuleDto>>> GetAttendanceRules([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetAttendanceRulesQuery(activeOnly)));

    /// <summary>Creates attendance rule.</summary>
    /// <remarks>Route: POST api/hr/attendance-rules. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("attendance-rules")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<AttendanceRuleDto>> CreateAttendanceRule([FromBody] CreateAttendanceRuleCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAttendanceRules), new { }, result);
    }

    /// <summary>Updates attendance rule.</summary>
    /// <remarks>Route: PUT api/hr/attendance-rules/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("attendance-rules/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<AttendanceRuleDto>> UpdateAttendanceRule(Guid id, [FromBody] UpdateAttendanceRuleCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // --------------------------------------------------------------- leave types

    /// <summary>Gets leave types.</summary>
    /// <remarks>Route: GET api/hr/leave-types. Requires the 'hr.view' permission.</remarks>
    [HttpGet("leave-types")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<LeaveTypeDto>>> GetLeaveTypes([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetLeaveTypesQuery(activeOnly)));

    /// <summary>Creates leave type.</summary>
    /// <remarks>Route: POST api/hr/leave-types. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("leave-types")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<LeaveTypeDto>> CreateLeaveType([FromBody] CreateLeaveTypeCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetLeaveTypes), new { }, result);
    }

    /// <summary>Updates leave type.</summary>
    /// <remarks>Route: PUT api/hr/leave-types/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("leave-types/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<LeaveTypeDto>> UpdateLeaveType(Guid id, [FromBody] UpdateLeaveTypeCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------------ pay components

    /// <summary>Gets pay components.</summary>
    /// <remarks>Route: GET api/hr/pay-components. Requires the 'hr.view' permission.</remarks>
    [HttpGet("pay-components")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<PayComponentDto>>> GetPayComponents([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetPayComponentsQuery(activeOnly)));

    /// <summary>Creates pay component.</summary>
    /// <remarks>Route: POST api/hr/pay-components. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("pay-components")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<PayComponentDto>> CreatePayComponent([FromBody] CreatePayComponentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetPayComponents), new { }, result);
    }

    /// <summary>Updates pay component.</summary>
    /// <remarks>Route: PUT api/hr/pay-components/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("pay-components/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<PayComponentDto>> UpdatePayComponent(Guid id, [FromBody] UpdatePayComponentCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------- employee pay components

    /// <summary>Gets employee pay components.</summary>
    /// <remarks>Route: GET api/hr/employee-pay-components. Requires the 'hr.view' permission.</remarks>
    [HttpGet("employee-pay-components")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<EmployeePayComponentDto>>> GetEmployeePayComponents(
        [FromQuery] Guid? employeeId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetEmployeePayComponentsQuery(employeeId, activeOnly)));

    /// <summary>Assigns employee pay component.</summary>
    /// <remarks>Route: POST api/hr/employee-pay-components. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("employee-pay-components")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<EmployeePayComponentDto>> AssignEmployeePayComponent(
        [FromBody] AssignEmployeePayComponentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetEmployeePayComponents), new { }, result);
    }

    /// <summary>Updates employee pay component.</summary>
    /// <remarks>Route: PUT api/hr/employee-pay-components/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("employee-pay-components/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<EmployeePayComponentDto>> UpdateEmployeePayComponent(
        Guid id, [FromBody] UpdateEmployeePayComponentCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    /// <summary>Removes employee pay component.</summary>
    /// <remarks>Route: DELETE api/hr/employee-pay-components/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpDelete("employee-pay-components/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<IActionResult> RemoveEmployeePayComponent(Guid id)
    {
        await _mediator.Send(new RemoveEmployeePayComponentCommand(id));
        return NoContent();
    }

    // ----------------------------------------------------------- contributions

    /// <summary>Gets contribution rules.</summary>
    /// <remarks>Route: GET api/hr/contribution-rules. Requires the 'hr.view' permission.</remarks>
    [HttpGet("contribution-rules")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<ContributionRuleDto>>> GetContributionRules([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetContributionRulesQuery(activeOnly)));

    /// <summary>Creates contribution rule.</summary>
    /// <remarks>Route: POST api/hr/contribution-rules. Requires the 'hr.manage' permission.</remarks>
    [HttpPost("contribution-rules")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<ContributionRuleDto>> CreateContributionRule(
        [FromBody] CreateContributionRuleCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetContributionRules), new { }, result);
    }

    /// <summary>Updates contribution rule.</summary>
    /// <remarks>Route: PUT api/hr/contribution-rules/{id:guid}. Requires the 'hr.manage' permission.</remarks>
    [HttpPut("contribution-rules/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrManage)]
    public async Task<ActionResult<ContributionRuleDto>> UpdateContributionRule(
        Guid id, [FromBody] UpdateContributionRuleCommand command)
        => Ok(await _mediator.Send(id != command.Id ? command with { Id = id } : command));

    // ------------------------------------------------------ accounting mappings

    /// <summary>Gets account mappings.</summary>
    /// <remarks>Route: GET api/hr/account-mappings. Requires the 'payroll.accounting' permission.</remarks>
    [HttpGet("account-mappings")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollAccounting)]
    public async Task<ActionResult<List<PayrollAccountMappingDto>>> GetAccountMappings()
        => Ok(await _mediator.Send(new GetPayrollAccountMappingsQuery()));

    /// <summary>Runs the upsert account mapping operation.</summary>
    /// <remarks>Route: POST api/hr/account-mappings. Requires the 'payroll.accounting' permission.</remarks>
    [HttpPost("account-mappings")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollAccounting)]
    public async Task<ActionResult<PayrollAccountMappingDto>> UpsertAccountMapping(
        [FromBody] UpsertPayrollAccountMappingCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAccountMappings), new { }, result);
    }

    // ------------------------------------------------------- employee HR profile

    /// <summary>Sets employee profile.</summary>
    /// <remarks>Route: PUT api/hr/employees/{id:guid}/profile. Requires the 'payroll.employees' permission.</remarks>
    [HttpPut("employees/{id:guid}/profile")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<EmployeeDto>> SetEmployeeProfile(Guid id, [FromBody] SetEmployeeHrProfileCommand command)
        => Ok(await _mediator.Send(new SetEmployeeHrProfileCommand(id, command.HrSectionId, command.JobPositionId,
            command.CostCenterId, command.HrShiftId, command.IsTaxExempt, command.IsInsuranceExempt)));

    // -------------------------------------------------------------- attendance

    /// <summary>Gets attendance.</summary>
    /// <remarks>Route: GET api/hr/attendance. Requires the 'hr.view' permission.</remarks>
    [HttpGet("attendance")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceEventDto>>> GetAttendance(
        [FromQuery] Guid? employeeId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] AttendanceStatus? status, [FromQuery] Guid? departmentId)
        => Ok(await _mediator.Send(new GetAttendanceQuery(employeeId, fromDate, toDate, status, departmentId)));

    /// <summary>Gets attendance summary.</summary>
    /// <remarks>Route: GET api/hr/attendance/summary. Requires the 'hr.view' permission.</remarks>
    [HttpGet("attendance/summary")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<AttendanceSummaryDto>>> GetAttendanceSummary(
        [FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, [FromQuery] Guid? departmentId)
        => Ok(await _mediator.Send(new GetAttendanceSummaryQuery(fromDate, toDate, departmentId)));

    /// <summary>Records attendance.</summary>
    /// <remarks>Route: POST api/hr/attendance. Requires the 'hr.attendance' permission.</remarks>
    [HttpPost("attendance")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<AttendanceEventDto>> RecordAttendance([FromBody] RecordAttendanceCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Records non working day.</summary>
    /// <remarks>Route: POST api/hr/attendance/non-working. Requires the 'hr.attendance' permission.</remarks>
    [HttpPost("attendance/non-working")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<AttendanceEventDto>> RecordNonWorkingDay([FromBody] RecordNonWorkingDayCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Generates attendance.</summary>
    /// <remarks>Route: POST api/hr/attendance/generate. Requires the 'hr.attendance' permission.</remarks>
    [HttpPost("attendance/generate")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<ActionResult<object>> GenerateAttendance([FromBody] GenerateAttendanceForPeriodCommand command)
        => Ok(new { created = await _mediator.Send(command) });

    /// <summary>Removes attendance.</summary>
    /// <remarks>Route: DELETE api/hr/attendance/{id:guid}. Requires the 'hr.attendance' permission.</remarks>
    [HttpDelete("attendance/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAttendance)]
    public async Task<IActionResult> RemoveAttendance(Guid id)
    {
        await _mediator.Send(new RemoveAttendanceEventCommand(id));
        return NoContent();
    }

    // ------------------------------------------------------------------- leave

    /// <summary>Gets leave requests.</summary>
    /// <remarks>Route: GET api/hr/leave-requests. Requires the 'hr.view' permission.</remarks>
    [HttpGet("leave-requests")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<LeaveRequestDto>>> GetLeaveRequests(
        [FromQuery] LeaveStatus? status, [FromQuery] Guid? employeeId)
        => Ok(await _mediator.Send(new GetLeaveRequestsQuery(status, employeeId)));

    /// <summary>Submits leave request.</summary>
    /// <remarks>Route: POST api/hr/leave-requests. Requires the 'hr.leave_request' permission.</remarks>
    [HttpPost("leave-requests")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveRequest)]
    public async Task<ActionResult<LeaveRequestDto>> SubmitLeaveRequest([FromBody] SubmitLeaveRequestCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Approves leave request.</summary>
    /// <remarks>Route: POST api/hr/leave-requests/{id:guid}/approve. Requires the 'hr.leave_approve' permission.</remarks>
    [HttpPost("leave-requests/{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveApprove)]
    public async Task<ActionResult<LeaveRequestDto>> ApproveLeaveRequest(Guid id, [FromBody] HrDecisionNote? note)
        => Ok(await _mediator.Send(new ApproveLeaveRequestCommand(id, note?.Note)));

    /// <summary>Rejects leave request.</summary>
    /// <remarks>Route: POST api/hr/leave-requests/{id:guid}/reject. Requires the 'hr.leave_approve' permission.</remarks>
    [HttpPost("leave-requests/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveApprove)]
    public async Task<ActionResult<LeaveRequestDto>> RejectLeaveRequest(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new RejectLeaveRequestCommand(id, request.Reason ?? string.Empty)));

    /// <summary>Cancels leave request.</summary>
    /// <remarks>Route: POST api/hr/leave-requests/{id:guid}/cancel. Requires the 'hr.leave_request' permission.</remarks>
    [HttpPost("leave-requests/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLeaveRequest)]
    public async Task<ActionResult<LeaveRequestDto>> CancelLeaveRequest(Guid id, [FromBody] HrDecisionNote? note)
        => Ok(await _mediator.Send(new CancelLeaveRequestCommand(id, note?.Note)));

    // ---------------------------------------------------------------- overtime

    /// <summary>Gets overtime.</summary>
    /// <remarks>Route: GET api/hr/overtime. Requires the 'hr.view' permission.</remarks>
    [HttpGet("overtime")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<OvertimeEntryDto>>> GetOvertime(
        [FromQuery] OvertimeStatus? status, [FromQuery] Guid? employeeId,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        => Ok(await _mediator.Send(new GetOvertimeEntriesQuery(status, employeeId, fromDate, toDate)));

    /// <summary>Submits overtime.</summary>
    /// <remarks>Route: POST api/hr/overtime. Requires the 'hr.overtime_request' permission.</remarks>
    [HttpPost("overtime")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeRequest)]
    public async Task<ActionResult<OvertimeEntryDto>> SubmitOvertime([FromBody] SubmitOvertimeCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Approves overtime.</summary>
    /// <remarks>Route: POST api/hr/overtime/{id:guid}/approve. Requires the 'hr.overtime_approve' permission.</remarks>
    [HttpPost("overtime/{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeApprove)]
    public async Task<ActionResult<OvertimeEntryDto>> ApproveOvertime(Guid id)
        => Ok(await _mediator.Send(new ApproveOvertimeCommand(id)));

    /// <summary>Rejects overtime.</summary>
    /// <remarks>Route: POST api/hr/overtime/{id:guid}/reject. Requires the 'hr.overtime_approve' permission.</remarks>
    [HttpPost("overtime/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrOvertimeApprove)]
    public async Task<ActionResult<OvertimeEntryDto>> RejectOvertime(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new RejectOvertimeCommand(id, request.Reason ?? string.Empty)));

    // ---------------------------------------------------------------- advances

    /// <summary>Gets advances.</summary>
    /// <remarks>Route: GET api/hr/advances. Requires the 'hr.view' permission.</remarks>
    [HttpGet("advances")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrView)]
    public async Task<ActionResult<List<EmployeeAdvanceDto>>> GetAdvances(
        [FromQuery] Guid? employeeId, [FromQuery] AdvanceStatus? status)
        => Ok(await _mediator.Send(new GetEmployeeAdvancesQuery(employeeId, status)));

    /// <summary>Creates advance.</summary>
    /// <remarks>Route: POST api/hr/advances. Requires the 'hr.advances' permission.</remarks>
    [HttpPost("advances")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAdvancesManage)]
    public async Task<ActionResult<EmployeeAdvanceDto>> CreateAdvance([FromBody] CreateEmployeeAdvanceCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Cancels advance.</summary>
    /// <remarks>Route: POST api/hr/advances/{id:guid}/cancel. Requires the 'hr.advances' permission.</remarks>
    [HttpPost("advances/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrAdvancesManage)]
    public async Task<ActionResult<EmployeeAdvanceDto>> CancelAdvance(Guid id, [FromBody] HrDecisionRequest request)
        => Ok(await _mediator.Send(new CancelEmployeeAdvanceCommand(id, request.Reason ?? string.Empty)));

    // --------------------------------------------------------------- reporting

    /// <summary>Labor cost by department or cost center for a payroll period (spec section 40).</summary>
    [HttpGet("labor-cost")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.HrLaborCost)]
    public async Task<ActionResult<LaborCostReportDto>> GetLaborCost(
        [FromQuery] int periodYear, [FromQuery] int periodMonth, [FromQuery] bool groupByCostCenter = false)
        => Ok(await _mediator.Send(new GetLaborCostReportQuery(periodYear, periodMonth, groupByCostCenter)));

    /// <summary>Labor cost as Excel or PDF (spec section 40).</summary>
    [HttpGet("labor-cost/export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
/// <param name="Note">The note.</param>
public record HrDecisionNote(string? Note = null);

/// <summary>Body for a rejection or cancellation - the reason is mandatory server-side.</summary>
/// <param name="Reason">The reason.</param>
public record HrDecisionRequest(string? Reason = null);
