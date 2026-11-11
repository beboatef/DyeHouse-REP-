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
    /// <summary>Initializes a new instance of the SuppliesController class.</summary>
    public SuppliesController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists operating supplies.</summary>
    /// <remarks>Route: GET api/supplies. Requires the 'supplies.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesView)]
    [ProducesResponseType(typeof(List<SupplyIssueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<SupplyIssueDto>>> Get(
        [FromQuery] SupplyIssueStatus? status, [FromQuery] Guid? warehouseId,
        [FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetSupplyIssuesQuery(status, warehouseId, search, from, to)));

    /// <summary>Gets by id.</summary>
    /// <remarks>Route: GET api/supplies/{id:guid}. Requires the 'supplies.view' permission.</remarks>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesView)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplyIssueDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetSupplyIssueByIdQuery(id)));

    /// <summary>Supply issues as Excel or PDF, honouring the same filters as the list (spec section 27).</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        return ExportFileHelper.ToFile(export, format, "Operating Supplies Issues", null, headers, rows, "supply-issues", "SupplyIssues");
    }

    /// <summary>One supply issue as a printable voucher (spec section 39).</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    /// <summary>Creates operating supplies.</summary>
    /// <remarks>Route: POST api/supplies. Requires the 'supplies.issue' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SupplyIssueDto>> Create([FromBody] CreateSupplyIssueCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Adds line.</summary>
    /// <remarks>Route: POST api/supplies/{id:guid}/lines. Requires the 'supplies.issue' permission.</remarks>
    [HttpPost("{id:guid}/lines")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SupplyIssueDto>> AddLine(Guid id, [FromBody] AddSupplyIssueLineCommand command)
        => Ok(await _mediator.Send(command with { SupplyIssueId = id }));

    /// <summary>Removes line.</summary>
    /// <remarks>Route: DELETE api/supplies/{id:guid}/lines/{lineId:guid}. Requires the 'supplies.issue' permission.</remarks>
    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplyIssueDto>> RemoveLine(Guid id, Guid lineId)
        => Ok(await _mediator.Send(new RemoveSupplyIssueLineCommand(id, lineId)));

    /// <summary>Posts operating supplies.</summary>
    /// <remarks>Route: POST api/supplies/{id:guid}/post. Requires the 'supplies.issue' permission.</remarks>
    [HttpPost("{id:guid}/post")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesIssue)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SupplyIssueDto>> Post(Guid id)
        => Ok(await _mediator.Send(new PostSupplyIssueCommand(id)));

    /// <summary>Cancels operating supplies.</summary>
    /// <remarks>Route: POST api/supplies/{id:guid}/cancel. Requires the 'supplies.cancel' permission.</remarks>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliesCancel)]
    [ProducesResponseType(typeof(SupplyIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SupplyIssueDto>> Cancel(Guid id, [FromBody] CancelSupplyIssueBody body)
        => Ok(await _mediator.Send(new CancelSupplyIssueCommand(id, body.Reason)));
}

/// <summary>Request payload to cancel supply issue.</summary>
/// <param name="Reason">The reason.</param>
public record CancelSupplyIssueBody(string Reason);
