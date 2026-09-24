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
    public ReportBuilderController(ISender mediator) => _mediator = mediator;

    [HttpGet("entities")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<List<ReportableEntityDto>>> GetEntities() => Ok(await _mediator.Send(new GetReportableEntitiesQuery()));

    [HttpPost("run")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<ReportResultDto>> Run([FromBody] RunReportCommand command) => Ok(await _mediator.Send(command));

    [HttpGet("templates")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsView)]
    public async Task<ActionResult<List<SavedReportTemplateDto>>> GetTemplates() => Ok(await _mediator.Send(new GetReportTemplatesQuery()));

    [HttpPost("templates")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<ActionResult<SavedReportTemplateDto>> SaveTemplate([FromBody] SaveReportTemplateCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetTemplates), new { }, result);
    }

    [HttpPost("run/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> RunPdf([FromBody] RunReportCommand command, [FromServices] IReportExportService export)
    {
        var result = await _mediator.Send(command);
        var rows = result.Rows.Select(r => r.Cast<object?>().ToArray()).ToList();
        var bytes = export.GeneratePdf("تقرير مخصص", command.EntityKey, result.Headers, rows);
        return File(bytes, "application/pdf", "custom-report.pdf");
    }

    [HttpPost("run/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> RunExcel([FromBody] RunReportCommand command, [FromServices] IReportExportService export)
    {
        var result = await _mediator.Send(command);
        var rows = result.Rows.Select(r => r.Cast<object?>().ToArray()).ToList();
        var bytes = export.GenerateExcel(command.EntityKey, result.Headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "custom-report.xlsx");
    }
}
