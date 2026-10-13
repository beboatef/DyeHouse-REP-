using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.Commands;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Application.FormationRequests.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Formation Request - طلب تشكيل (spec sections 28-33).
/// One request carries many groups/cells, each with its own specification snapshot taken from the reusable
/// specification master. Approving freezes those specifications. A Formation Request is an INDEPENDENT document:
/// it never creates, links to, or converts into a Job Order, and a Job Order never references it.
/// </summary>
[ApiController]
[Route("api/formation-requests")]
[Authorize]
public class FormationRequestsController : ControllerBase
{
    private readonly ISender _mediator;
    public FormationRequestsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationView)]
    [ProducesResponseType(typeof(List<FormationRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FormationRequestDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] Guid? rawMessageId,
        [FromQuery] FormationRequestStatus? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetFormationRequestsQuery(customerId, itemId, rawMessageId, status, from, to)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationView)]
    public async Task<ActionResult<FormationRequestDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetFormationRequestByIdQuery(id)));

    /// <summary>The request's chain: Customer -&gt; Raw Material Message -&gt; Formation Request (independent of production).</summary>
    [HttpGet("{id:guid}/traceability")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationViewTraceability)]
    public async Task<ActionResult<FormationTraceabilityDto>> GetTraceability(Guid id)
        => Ok(await _mediator.Send(new GetFormationRequestTraceabilityQuery(id)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationCreate)]
    [ProducesResponseType(typeof(FormationRequestDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FormationRequestDto>> Create([FromBody] CreateFormationRequestCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    public async Task<ActionResult<FormationRequestDto>> Update(Guid id, [FromBody] UpdateFormationRequestCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationSubmit)]
    public async Task<ActionResult<FormationRequestDto>> Submit(Guid id)
        => Ok(await _mediator.Send(new SubmitFormationRequestCommand(id)));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationApprove)]
    public async Task<ActionResult<FormationRequestDto>> Approve(Guid id)
        => Ok(await _mediator.Send(new ApproveFormationRequestCommand(id)));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationReject)]
    public async Task<ActionResult<FormationRequestDto>> Reject(Guid id, [FromBody] FormationReasonRequest request)
        => Ok(await _mediator.Send(new RejectFormationRequestCommand(id, request.Reason)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationCancel)]
    public async Task<ActionResult<FormationRequestDto>> Cancel(Guid id, [FromBody] FormationReasonRequest request)
        => Ok(await _mediator.Send(new CancelFormationRequestCommand(id, request.Reason)));

    /// <summary>
    /// Creates a REVISION of a submitted/approved request that can no longer be edited in place. The original is
    /// left exactly as it is (its approval history intact) and the revision starts as a fresh Draft that must be
    /// submitted and approved on its own. Gated by the configurable formation.edit permission.
    /// </summary>
    [HttpPost("{id:guid}/revise")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    [ProducesResponseType(typeof(FormationRequestDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FormationRequestDto>> Revise(Guid id)
        => Ok(await _mediator.Send(new ReviseFormationRequestCommand(id)));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteFormationRequestCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Records a basin's ACTUAL weighed quantity (spec sections 8 and 10) before it is
    /// converted - the Job Order quantity is taken from this figure, never from the plan.
    /// </summary>
    [HttpPost("{id:guid}/basins/{basinId:guid}/actual-quantity")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    public async Task<ActionResult<FormationRequestDto>> RecordBasinActualQuantity(
        Guid id, Guid basinId, [FromBody] RecordBasinActualQuantityRequest request)
        => Ok(await _mediator.Send(new RecordFormationBasinActualQuantityCommand(
            id, request.GroupId, basinId, request.ActualQuantity)));

    /// <summary>
    /// Moves an approved request into production (spec section 32). A Formation Request is an
    /// independent document: this never creates or changes a Job Order and never touches stock.
    /// </summary>
    [HttpPost("{id:guid}/start-production")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    public async Task<ActionResult<FormationRequestDto>> StartProduction(Guid id)
        => Ok(await _mediator.Send(new StartFormationProductionCommand(id)));

    /// <summary>
    /// Records produced quantity against one group of a request that has NO basins (spec sections 32-33).
    /// Accumulates - never overwrites - and re-derives the request's own completion status.
    /// </summary>
    [HttpPost("{id:guid}/groups/{groupId:guid}/production")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    public async Task<ActionResult<FormationRequestDto>> RecordGroupProduction(
        Guid id, Guid groupId, [FromBody] RecordGroupOutputRequest request)
        => Ok(await _mediator.Send(new RecordFormationGroupProductionCommand(id, groupId, request.Quantity)));

    /// <summary>
    /// Records produced quantity against ONE basin of a group measured per basin (spec sections 10-11, 32).
    /// The group's produced figure is re-derived from its basins, so it can never exceed what they hold.
    /// </summary>
    [HttpPost("{id:guid}/basins/{basinId:guid}/production")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationEdit)]
    public async Task<ActionResult<FormationRequestDto>> RecordBasinProduction(
        Guid id, Guid basinId, [FromBody] RecordBasinOutputRequest request)
        => Ok(await _mediator.Send(new RecordFormationBasinProductionCommand(
            id, request.GroupId, basinId, request.Quantity)));

    [HttpGet("export/excel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationExport)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] Guid? customerId, [FromQuery] FormationRequestStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromServices] IReportExportService export)
    {
        var requests = await _mediator.Send(new GetFormationRequestsQuery(customerId, null, null, status, from, to));
        var headers = new List<string>
        {
            "RequestNumber", "Date", "Customer", "Item", "Message", "Groups", "Basins", "TotalQuantity", "Unit", "Status"
        };

        var rows = requests.Select(r => new object?[]
        {
            r.RequestNumber, r.RequestDate.ToString("yyyy-MM-dd"), r.CustomerCode, r.ItemCode, r.MessageNumber,
            r.Groups.Count, r.BasinCount, r.TotalQuantity, r.Unit.ToString(), r.Status.ToString()
        }).ToList();

        var bytes = export.GenerateExcel("FormationRequests", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "formation-requests.xlsx");
    }

    /// <summary>The same register as a printable document (spec section 39), driven by the same filters.</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationExport)]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? customerId, [FromQuery] FormationRequestStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var requests = await _mediator.Send(new GetFormationRequestsQuery(customerId, null, null, status, from, to));
        var headers = new List<string>
        {
            "RequestNumber", "Date", "Customer", "Item", "Message", "Groups", "TotalQuantity", "Unit", "Status"
        };

        var rows = requests.Select(r => new object?[]
        {
            r.RequestNumber, r.RequestDate.ToString("yyyy-MM-dd"), r.CustomerCode, r.ItemCode, r.MessageNumber,
            r.Groups.Count, r.TotalQuantity, r.Unit.ToString(), r.Status.ToString()
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Customer Formation Requests", "DyeHouse ERP",
            headers, rows, "formation-requests", "FormationRequests");
    }

    /// <summary>Printable formation request with its group/cell specification table (spec sections 49-50).</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationPrint)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] IReportExportService export)
    {
        var request = await _mediator.Send(new GetFormationRequestByIdQuery(id));

        var headers = new List<string>
        {
            "Group", "Basin", "Quantity", "Unit", "Tubs", "Color", "Width(cm)", "m/kg", "g/m2", "TubFormat", "Specification"
        };

        // One printed row per basin (spec sections 10-11); a group without basins prints one row carrying
        // its own figures, so the document never invents a basin the planner did not create.
        var rows = request.Groups.SelectMany(g => g.Basins.Count > 0
                ? g.Basins.Select(b => new object?[]
                {
                    g.GroupNumber, b.BasinNumber, b.PlannedQuantity, b.Unit.ToString(), b.TubCount, b.Color,
                    b.WidthCm ?? g.WidthCm, b.MetersPerKg ?? g.MetersPerKg, b.Gsm ?? g.Gsm,
                    b.TubFormat ?? g.TubFormat, b.SpecificationTemplateName ?? g.SpecificationTemplateName
                })
                : new[] { new object?[]
                {
                    g.GroupNumber, (object?)null, g.PlannedQuantity, g.Unit.ToString(), g.TubCount, g.Color,
                    g.WidthCm, g.MetersPerKg, g.Gsm, g.TubFormat, g.SpecificationTemplateName
                } })
            .ToList();

        var subtitle = $"{request.CustomerCode} - {request.CustomerName} | {request.ItemCode} | {request.Status}";
        var bytes = export.GeneratePdf($"Formation Request {request.RequestNumber}", subtitle, headers, rows);

        return File(bytes, "application/pdf", $"formation-request-{request.RequestNumber}.pdf");
    }
}

public record FormationReasonRequest(string Reason);

public record RecordBasinActualQuantityRequest(Guid GroupId, decimal ActualQuantity);

public record RecordGroupOutputRequest(decimal Quantity);

public record RecordBasinOutputRequest(Guid GroupId, decimal Quantity);
