using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReportBuilder.Commands;
using DyeHouseERP.Application.ReportBuilder.DTOs;
using DyeHouseERP.Application.ReportBuilder.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Custom report builder (spec section 36) - pick a whitelisted entity, whitelisted columns, run, save as a template, export.</summary>
[ApiController]
[Route("api/report-builder")]
[Authorize]
public class ReportBuilderController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the ReportBuilderController class.</summary>
    public ReportBuilderController(ISender mediator) => _mediator = mediator;

    /// <summary>Gets entities.</summary>
    /// <remarks>Route: GET api/report-builder/entities. Requires the 'reports.view' permission.</remarks>
    [HttpGet("entities")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<List<ReportableEntityDto>>> GetEntities() => Ok(await _mediator.Send(new GetReportableEntitiesQuery()));

    /// <summary>Runs custom report builder.</summary>
    /// <remarks>Route: POST api/report-builder/run. Requires the 'reports.view' permission.</remarks>
    [HttpPost("run")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<ReportResultDto>> Run([FromBody] RunReportCommand command) => Ok(await _mediator.Send(command));

    /// <summary>Gets templates.</summary>
    /// <remarks>Route: GET api/report-builder/templates. Requires the 'reports.view' permission.</remarks>
    [HttpGet("templates")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<List<SavedReportTemplateDto>>> GetTemplates() => Ok(await _mediator.Send(new GetReportTemplatesQuery()));

    /// <summary>Runs the save template operation.</summary>
    /// <remarks>Route: POST api/report-builder/templates. Requires the 'reports.export' permission.</remarks>
    [HttpPost("templates")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<ActionResult<SavedReportTemplateDto>> SaveTemplate([FromBody] SaveReportTemplateCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetTemplates), new { }, result);
    }

    /// <summary>Runs pdf.</summary>
    /// <remarks>Route: POST api/report-builder/run/pdf. Requires the 'reports.export' permission.</remarks>
    [HttpPost("run/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> RunPdf([FromBody] RunReportCommand command, [FromServices] IReportExportService export)
    {
        var result = await _mediator.Send(command);
        var rows = result.Rows.Select(r => r.Cast<object?>().ToArray()).ToList();
        var bytes = export.GeneratePdf("تقرير مخصص", command.EntityKey, result.Headers, rows);
        return File(bytes, "application/pdf", "custom-report.pdf");
    }

    /// <summary>Runs excel.</summary>
    /// <remarks>Route: POST api/report-builder/run/excel. Requires the 'reports.export' permission.</remarks>
    [HttpPost("run/excel")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> RunExcel([FromBody] RunReportCommand command, [FromServices] IReportExportService export)
    {
        var result = await _mediator.Send(command);
        var rows = result.Rows.Select(r => r.Cast<object?>().ToArray()).ToList();
        var bytes = export.GenerateExcel(command.EntityKey, result.Headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "custom-report.xlsx");
    }
}
