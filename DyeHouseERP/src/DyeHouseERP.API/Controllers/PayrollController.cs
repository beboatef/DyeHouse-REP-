using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
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
    /// <summary>Initializes a new instance of the PayrollController class.</summary>
    public PayrollController(ISender mediator) => _mediator = mediator;

    // -------------------------------------------------------- departments

    /// <summary>Gets departments.</summary>
    /// <remarks>Route: GET api/Payroll/departments. Requires the 'payroll.view' permission.</remarks>
    [HttpGet("departments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetDepartmentsQuery(activeOnly, search)));

    /// <summary>Creates department.</summary>
    /// <remarks>Route: POST api/Payroll/departments. Requires the 'payroll.employees' permission.</remarks>
    [HttpPost("departments")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment([FromBody] CreateDepartmentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetDepartments), new { }, result);
    }

    /// <summary>Updates department.</summary>
    /// <remarks>Route: PUT api/Payroll/departments/{id:guid}. Requires the 'payroll.employees' permission.</remarks>
    [HttpPut("departments/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(DepartmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DepartmentDto>> UpdateDepartment(Guid id, [FromBody] UpdateDepartmentCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    // ---------------------------------------------------------- employees

    /// <summary>Gets employees.</summary>
    /// <remarks>Route: GET api/Payroll/employees. Requires the 'payroll.view' permission.</remarks>
    [HttpGet("employees")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<EmployeeDto>>> GetEmployees(
        [FromQuery] Guid? departmentId, [FromQuery] EmployeeStatus? status,
        [FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetEmployeesQuery(departmentId, status, activeOnly, search)));

    /// <summary>Gets employee.</summary>
    /// <remarks>Route: GET api/Payroll/employees/{id:guid}. Requires the 'payroll.view' permission.</remarks>
    [HttpGet("employees/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmployeeDto>> GetEmployee(Guid id)
        => Ok(await _mediator.Send(new GetEmployeeByIdQuery(id)));

    /// <summary>Creates employee.</summary>
    /// <remarks>Route: POST api/Payroll/employees. Requires the 'payroll.employees' permission.</remarks>
    [HttpPost("employees")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee([FromBody] CreateEmployeeCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetEmployee), new { id = result.Id }, result);
    }

    /// <summary>Updates employee.</summary>
    /// <remarks>Route: PUT api/Payroll/employees/{id:guid}. Requires the 'payroll.employees' permission.</remarks>
    [HttpPut("employees/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmployeeDto>> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    // ------------------------------- employees export / import (spec section 38)

    /// <summary>Employee master as Excel or PDF, honouring the same filters as GET.</summary>
    [HttpGet("employees/export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportEmployees(
        [FromQuery] Guid? departmentId, [FromQuery] EmployeeStatus? status,
        [FromQuery] bool? activeOnly, [FromQuery] string? search,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var employees = await _mediator.Send(new GetEmployeesQuery(departmentId, status, activeOnly, search));
        var headers = new[] { "Code", "NameAr", "NameEn", "Department", "Job title", "Basic salary", "Hire date", "Phone", "Status" };
        var rows = employees.Select(e => new object?[]
        {
            e.Code, e.NameAr, e.NameEn, $"{e.DepartmentCode} - {e.DepartmentName}", e.JobTitle,
            e.BasicSalary, e.HireDate.ToString("yyyy-MM-dd"), e.Phone, e.Status.ToString()
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Employees", null, headers, rows, "employees", "Employees");
    }

    /// <summary>Step 1 of the employee import workflow: the template with the exact expected columns.</summary>
    [HttpGet("employees/import/template")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    public IActionResult GetEmployeeImportTemplate([FromServices] IReportExportService export)
        => ExportFileHelper.Template(export, "Employees", EmployeeImportTemplate.Headers, EmployeeImportTemplate.SampleRows(), "employees-import-template");

    /// <summary>Step 2: validate the upload (departments, salary and dates) and report row errors. Writes nothing.</summary>
    [HttpPost("employees/import/preview")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewEmployeeImport(IFormFile file)
        => Ok(await _mediator.Send(new PreviewEmployeeImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Step 3: confirm - re-validates and creates only the valid new rows.</summary>
    [HttpPost("employees/import/execute")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteEmployeeImport(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteEmployeeImportCommand(await ExportFileHelper.ReadUploadAsync(file), false)));

    /// <summary>Preview of the update-capable import - payroll.employees is required to overwrite an existing employee.</summary>
    [HttpPost("employees/import/preview-update")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewEmployeeImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new PreviewEmployeeImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));

    /// <summary>Confirm of the update-capable import (spec rule: no silent overwrite).</summary>
    [HttpPost("employees/import/execute-update")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollManageEmployees)]
    public async Task<ActionResult<ImportExecuteResultDto>> ExecuteEmployeeImportWithUpdate(IFormFile file)
        => Ok(await _mediator.Send(new ExecuteEmployeeImportCommand(await ExportFileHelper.ReadUploadAsync(file), true)));

    // -------------------------------------------------------- payroll runs

    /// <summary>Gets runs.</summary>
    /// <remarks>Route: GET api/Payroll/runs. Requires the 'payroll.view' permission.</remarks>
    [HttpGet("runs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(List<PayrollRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<PayrollRunDto>>> GetRuns([FromQuery] int? periodYear, [FromQuery] PayrollRunStatus? status)
        => Ok(await _mediator.Send(new GetPayrollRunsQuery(periodYear, status)));

    /// <summary>Gets run.</summary>
    /// <remarks>Route: GET api/Payroll/runs/{id:guid}. Requires the 'payroll.view' permission.</remarks>
    [HttpGet("runs/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayrollRunDto>> GetRun(Guid id)
        => Ok(await _mediator.Send(new GetPayrollRunByIdQuery(id)));

    /// <summary>Creates run.</summary>
    /// <remarks>Route: POST api/Payroll/runs. Requires the 'payroll.create' permission.</remarks>
    [HttpPost("runs")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> CreateRun([FromBody] CreatePayrollRunCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRun), new { id = result.Id }, result);
    }

    /// <summary>Updates run line.</summary>
    /// <remarks>Route: PUT api/Payroll/runs/{id:guid}/lines/{lineId:guid}. Requires the 'payroll.create' permission.</remarks>
    [HttpPut("runs/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> UpdateRunLine(Guid id, Guid lineId, [FromBody] UpdatePayrollRunLineCommand command)
    {
        if (id != command.RunId || lineId != command.LineId) command = command with { RunId = id, LineId = lineId };
        return Ok(await _mediator.Send(command));
    }

    /// <summary>Removes run line.</summary>
    /// <remarks>Route: DELETE api/Payroll/runs/{id:guid}/lines/{lineId:guid}. Requires the 'payroll.create' permission.</remarks>
    [HttpDelete("runs/{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollRunDto>> RemoveRunLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemovePayrollRunLineCommand(id, lineId)));

    /// <summary>Sets run account.</summary>
    /// <remarks>Route: PUT api/Payroll/runs/{id:guid}/account. Requires the 'payroll.create' permission.</remarks>
    [HttpPut("runs/{id:guid}/account")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> SetRunAccount(Guid id, [FromBody] SetPayrollAccountRequest request)
        => Ok(await _mediator.Send(new SetPayrollRunAccountCommand(id, request.TreasuryAccountId)));

    /// <summary>Approves run.</summary>
    /// <remarks>Route: POST api/Payroll/runs/{id:guid}/approve. Requires the 'payroll.approve' permission.</remarks>
    [HttpPost("runs/{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollApprove)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> ApproveRun(Guid id)
        => Ok(await _mediator.Send(new ApprovePayrollRunCommand(id)));

    /// <summary>Posts the run: the net total leaves the selected treasury/bank account.</summary>
    [HttpPost("runs/{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollPost)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> PostRun(Guid id)
        => Ok(await _mediator.Send(new PostPayrollRunCommand(id)));

    /// <summary>Cancels run.</summary>
    /// <remarks>Route: POST api/Payroll/runs/{id:guid}/cancel. Requires the 'payroll.cancel' permission.</remarks>
    [HttpPost("runs/{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCancel)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> CancelRun(Guid id, [FromBody] CancelPayrollRequest request)
        => Ok(await _mediator.Send(new CancelPayrollRunCommand(id, request.Reason)));

    /// <summary>
    /// Calculates a draft/calculated run's amounts from the configured masters (attendance, approved
    /// overtime, active advances, pay components and contribution rules). No financial effect: it only
    /// fills the run's own editable lines and moves it to Calculated (spec sections 35-39).
    /// </summary>
    [HttpPost("runs/{id:guid}/calculate")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> CalculateRun(Guid id)
        => Ok(await _mediator.Send(new CalculatePayrollRunCommand(id)));

    /// <summary>
    /// Submits a CALCULATED run for review (spec section 39): Calculated -&gt; UnderReview. Approval needs a
    /// different authority (payroll.approve), so the preparer never approves their own run.
    /// </summary>
    [HttpPost("runs/{id:guid}/submit")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCreate)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> SubmitRun(Guid id)
        => Ok(await _mediator.Send(new SubmitPayrollRunForReviewCommand(id)));

    /// <summary>
    /// Returns an UNDER-REVIEW run to the calculated stage so a line can be corrected (spec section 39):
    /// UnderReview -&gt; Calculated. The reviewing authority (payroll.approve) is the one that sends it back.
    /// </summary>
    [HttpPost("runs/{id:guid}/return")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollApprove)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> ReturnRun(Guid id, [FromBody] ReturnPayrollRunRequest? request)
        => Ok(await _mediator.Send(new ReturnPayrollRunForCorrectionCommand(id, request?.Notes)));

    /// <summary>
    /// Reverses a POSTED run (spec section 39): posts the equal-and-opposite IN treasury row, marks the run
    /// Reversed and opens a linked ADJUSTMENT draft for the same period carrying the original figures. Nothing
    /// posted is edited or deleted, and the reason is mandatory. A non-posted run is refused.
    /// </summary>
    [HttpPost("runs/{id:guid}/reverse")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollCancel)]
    [ProducesResponseType(typeof(PayrollRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PayrollRunDto>> ReverseRun(Guid id, [FromBody] ReversePayrollRunRequest request)
        => Ok(await _mediator.Send(new ReversePayrollRunCommand(id, request.Reason)));

    /// <summary>
    /// Payslip PDF for one employee within a run (spec section 49). Built from
    /// the run's own line, so the printed figures are exactly what was approved
    /// and posted - never recomputed from the employee master.
    /// </summary>
    [HttpGet("runs/{id:guid}/payslips/{lineId:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    public async Task<IActionResult> GetPayslipPdf(Guid id, Guid lineId, [FromServices] IReportExportService export)
    {
        var run = await _mediator.Send(new GetPayrollRunByIdQuery(id));
        var line = run.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return NotFound();

        var headers = new[] { "Item", "Amount" };
        var rows = new List<object?[]>
        {
            new object?[] { "Employee code", line.EmployeeCode },
            new object?[] { "Employee", line.EmployeeName },
            new object?[] { "Department", line.DepartmentName },
            new object?[] { "Basic salary", line.BasicSalary },
            new object?[] { "Allowances", line.Allowances },
            new object?[] { "Gross", line.GrossPay },
            new object?[] { "Deductions", line.Deductions },
            new object?[] { "Net pay", line.NetPay }
        };

        var subtitle = $"Payslip {run.PeriodMonth:00}/{run.PeriodYear} - run {run.RunNumber}";
        var pdf = export.GeneratePdf($"Payslip - {line.EmployeeName}", subtitle, headers, rows);
        return File(pdf, "application/pdf", $"payslip-{line.EmployeeCode}-{run.PeriodYear}{run.PeriodMonth:00}.pdf");
    }

    /// <summary>The whole run as a printable payroll sheet (spec section 49).</summary>
    [HttpGet("runs/{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    public async Task<IActionResult> GetRunPdf(Guid id, [FromServices] IReportExportService export)
    {
        var run = await _mediator.Send(new GetPayrollRunByIdQuery(id));

        var headers = new[] { "Employee code", "Employee", "Department", "Basic", "Allowances", "Deductions", "Net" };
        var rows = run.Lines.Select(l => new object?[]
        {
            l.EmployeeCode, l.EmployeeName, l.DepartmentName, l.BasicSalary, l.Allowances, l.Deductions, l.NetPay
        }).ToList();

        rows.Add(new object?[] { string.Empty, "TOTAL", string.Empty, string.Empty, string.Empty, run.TotalDeductions, run.TotalNet });

        var pdf = export.GeneratePdf($"Payroll Run {run.RunNumber}", $"{run.PeriodMonth:00}/{run.PeriodYear} - {run.Status}", headers, rows);
        return File(pdf, "application/pdf", $"payroll-{run.RunNumber}.pdf");
    }

    /// <summary>Payroll run as Excel for accounting (spec section 49).</summary>
    [HttpGet("runs/{id:guid}/excel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PayrollView)]
    public async Task<IActionResult> GetRunExcel(Guid id, [FromServices] IReportExportService export)
    {
        var run = await _mediator.Send(new GetPayrollRunByIdQuery(id));

        var headers = new[] { "Employee code", "Employee", "Department", "Basic", "Allowances", "Deductions", "Net" };
        var rows = run.Lines.Select(l => new object?[]
        {
            l.EmployeeCode, l.EmployeeName, l.DepartmentName, l.BasicSalary, l.Allowances, l.Deductions, l.NetPay
        }).ToList();

        var xlsx = export.GenerateExcel($"Payroll {run.PeriodYear}-{run.PeriodMonth:00}", headers, rows);
        return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"payroll-{run.RunNumber}.xlsx");
    }
}

/// <summary>Body for selecting which account a payroll run is paid from.</summary>
/// <param name="TreasuryAccountId">The treasury account id.</param>
public record SetPayrollAccountRequest(Guid? TreasuryAccountId);

/// <summary>Body for cancelling a payroll run - a documented reason is mandatory (spec section 46).</summary>
/// <param name="Reason">The reason.</param>
public record CancelPayrollRequest(string Reason);

/// <summary>Body for returning a run under review - the note is optional.</summary>
/// <param name="Notes">The notes.</param>
public record ReturnPayrollRunRequest(string? Notes = null);

/// <summary>Body for reversing a posted run - the reason is mandatory (spec section 39).</summary>
/// <param name="Reason">The reason.</param>
public record ReversePayrollRunRequest(string Reason);
