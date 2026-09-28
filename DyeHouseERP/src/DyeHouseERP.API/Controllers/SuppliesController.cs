using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Supplies.Commands;
using DyeHouseERP.Application.Supplies.DTOs;
using DyeHouseERP.Application.Supplies.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Operating supplies internal issue (spec section 27): spare parts, winding
/// and packaging consumables, maintenance supplies handed to a department or
/// another internal user. Stock moves through the normal material ledger - the
/// cost is NOT charged to a Job Order unless someone adds it explicitly.
/// </summary>
[ApiController]
[Route("api/supplies")]
[Authorize]
public class SuppliesController : ControllerBase
{
    private readonly ISender _mediator;
    public SuppliesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesView)]
    [ProducesResponseType(typeof(List<SupplyIssueDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplyIssueDto>>> Get(
        [FromQuery] SupplyIssueStatus? status, [FromQuery] Guid? warehouseId,
        [FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetSupplyIssuesQuery(status, warehouseId, search, from, to)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesView)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplyIssueDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetSupplyIssueByIdQuery(id)));

    /// <summary>Supply issues as Excel or PDF, honouring the same filters as the list (spec section 27).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] SupplyIssueStatus? status, [FromQuery] Guid? warehouseId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var issues = await _mediator.Send(new GetSupplyIssuesQuery(status, warehouseId, null, from, to));

        var headers = new[] { "Issue no.", "Date", "Warehouse", "Department", "Issued to", "Purpose", "Material", "Qty", "Unit", "Unit cost", "Line total", "Status" };
        var rows = issues.SelectMany(i => i.Lines.Select(l => new object?[]
        {
            i.IssueNumber, i.IssueDate.ToString("yyyy-MM-dd"), i.WarehouseName, i.DepartmentName, i.IssuedTo, i.Purpose,
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitCost, l.TotalCost, i.Status.ToString()
        })).ToList();

        return ExportFileHelper.ToFile(export, format, "Operating Supplies Issues", "DyeHouse ERP", headers, rows, "supply-issues", "SupplyIssues");
    }

    /// <summary>One supply issue as a printable voucher (spec section 39).</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] IReportExportService export)
    {
        var issue = await _mediator.Send(new GetSupplyIssueByIdQuery(id));

        var headers = new[] { "Material", "Qty", "Unit", "Unit cost", "Line total" };
        var rows = issue.Lines.Select(l => new object?[]
        {
            $"{l.MaterialCode} - {l.MaterialName}", l.Quantity, l.Unit.ToString(), l.UnitCost, l.TotalCost
        }).ToList();
        rows.Add(new object?[] { "", "", "", "Total", issue.TotalCost });

        var subtitle = $"{issue.WarehouseName}  |  {issue.IssueDate:yyyy-MM-dd}  |  {issue.DepartmentName ?? issue.IssuedTo}  |  {issue.Status}";
        return ExportFileHelper.ToPdf(export, $"صرف مستلزمات رقم {issue.IssueNumber}", subtitle, headers, rows, $"supply-issue-{issue.IssueNumber}");
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplyIssueDto>> Create([FromBody] CreateSupplyIssueCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/lines")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplyIssueDto>> AddLine(Guid id, [FromBody] AddSupplyIssueLineCommand command)
        => Ok(await _mediator.Send(command with { SupplyIssueId = id }));

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplyIssueDto>> RemoveLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemoveSupplyIssueLineCommand(id, lineId)));

    [HttpPost("{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplyIssueDto>> Post(Guid id)
        => Ok(await _mediator.Send(new PostSupplyIssueCommand(id)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesCancel)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplyIssueDto>> Cancel(Guid id, [FromBody] CancelSupplyIssueBody body)
        => Ok(await _mediator.Send(new CancelSupplyIssueCommand(id, body.Reason)));
}

public record CancelSupplyIssueBody(string Reason);
