using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Payroll &amp; wages (spec section 36): departments, employees, monthly payroll
/// runs and their lines. Deliberately INDEPENDENT of job-order costing - nothing
/// here is allocated to a job order unless a future explicit allocation is made.
///
/// Approval and posting are separate authorities: approving a run accepts the
/// amounts, posting releases the money and is the only step with a financial effect.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PayrollController : ControllerBase
{
    private readonly ISender _mediator;
    public PayrollController(ISender mediator) => _mediator = mediator;

    // -------------------------------------------------------- departments

    [HttpGet("departments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetDepartmentsQuery(activeOnly, search)));

    [HttpPost("departments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment([FromBody] CreateDepartmentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetDepartments), new { }, result);
    }

    [HttpPut("departments/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentDto>> UpdateDepartment(Guid id, [FromBody] UpdateDepartmentCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    // ---------------------------------------------------------- employees

    [HttpGet("employees")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<EmployeeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EmployeeDto>>> GetEmployees(
        [FromQuery] Guid? departmentId, [FromQuery] EmployeeStatus? status,
        [FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetEmployeesQuery(departmentId, status, activeOnly, search)));

    [HttpGet("employees/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmployeeDto>> GetEmployee(Guid id)
        => Ok(await _mediator.Send(new GetEmployeeByIdQuery(id)));

    [HttpPost("employees")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee([FromBody] CreateEmployeeCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetEmployee), new { id = result.Id }, result);
    }

    [HttpPut("employees/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmployeeDto>> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    // -------------------------------------------------------- payroll runs

    [HttpGet("runs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<PayrollRunDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PayrollRunDto>>> GetRuns([FromQuery] int? periodYear, [FromQuery] PayrollRunStatus? status)
        => Ok(await _mediator.Send(new GetPayrollRunsQuery(periodYear, status)));

    [HttpGet("runs/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> GetRun(Guid id)
        => Ok(await _mediator.Send(new GetPayrollRunByIdQuery(id)));

    [HttpPost("runs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PayrollRunDto>> CreateRun([FromBody] CreatePayrollRunCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRun), new { id = result.Id }, result);
    }

    [HttpPut("runs/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> UpdateRunLine(Guid id, Guid lineId, [FromBody] UpdatePayrollRunLineCommand command)
    {
        if (id != command.RunId || lineId != command.LineId) command = command with { RunId = id, LineId = lineId };
        return Ok(await _mediator.Send(command));
    }

    [HttpDelete("runs/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> RemoveRunLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemovePayrollRunLineCommand(id, lineId)));

    [HttpPut("runs/{id:guid}/account")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> SetRunAccount(Guid id, [FromBody] SetPayrollAccountRequest request)
        => Ok(await _mediator.Send(new SetPayrollRunAccountCommand(id, request.TreasuryAccountId)));

    [HttpPost("runs/{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollApprove)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> ApproveRun(Guid id)
        => Ok(await _mediator.Send(new ApprovePayrollRunCommand(id)));

    /// <summary>Posts the run: the net total leaves the selected treasury/bank account.</summary>
    [HttpPost("runs/{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollPost)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> PostRun(Guid id)
        => Ok(await _mediator.Send(new PostPayrollRunCommand(id)));

    [HttpPost("runs/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCancel)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollRunDto>> CancelRun(Guid id, [FromBody] CancelPayrollRequest request)
        => Ok(await _mediator.Send(new CancelPayrollRunCommand(id, request.Reason)));
}

/// <summary>Body for selecting which account a payroll run is paid from.</summary>
public record SetPayrollAccountRequest(Guid? TreasuryAccountId);

/// <summary>Body for cancelling a payroll run - a documented reason is mandatory (spec section 46).</summary>
public record CancelPayrollRequest(string Reason);
