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
/// specification master. Approving freezes those specifications; converting turns a group (or the whole request)
/// into a Job Order, keeping the Customer -&gt; Message -&gt; Request -&gt; Job Order chain navigable.
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

    /// <summary>The full chain: Customer -&gt; Raw Material Message -&gt; Formation Request -&gt; Job Order -&gt; Production -&gt; Ready Goods -&gt; Delivery.</summary>
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

    /// <summary>Converts the whole request, one group/cell, or one basin into a Job Order (spec sections 16, 31 and 10-11).</summary>
    [HttpPost("{id:guid}/convert-to-job-order")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationConvertToJobOrder)]
    public async Task<ActionResult<FormationRequestDto>> ConvertToJobOrder(Guid id, [FromBody] ConvertFormationRequestRequest request)
        => Ok(await _mediator.Send(new ConvertFormationRequestToJobOrderCommand(
            id, request.OrderDate ?? DateTime.UtcNow, request.JobOrderType, request.Priority,
            request.GroupId, request.BasinId, request.Color, request.Notes, request.CustomerReference)));

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
            "RequestNumber", "Date", "Customer", "Item", "Message", "Groups", "Basins", "TotalQuantity", "Unit", "Status", "JobOrder"
        };

        var rows = requests.Select(r => new object?[]
        {
            r.RequestNumber, r.RequestDate.ToString("yyyy-MM-dd"), r.CustomerCode, r.ItemCode, r.MessageNumber,
            r.Groups.Count, r.BasinCount, r.TotalQuantity, r.Unit.ToString(), r.Status.ToString(), r.ProductionOrderNumber
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
            "RequestNumber", "Date", "Customer", "Item", "Message", "Groups", "TotalQuantity", "Unit", "Status", "JobOrder"
        };

        var rows = requests.Select(r => new object?[]
        {
            r.RequestNumber, r.RequestDate.ToString("yyyy-MM-dd"), r.CustomerCode, r.ItemCode, r.MessageNumber,
            r.Groups.Count, r.TotalQuantity, r.Unit.ToString(), r.Status.ToString(), r.ProductionOrderNumber
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

public record ConvertFormationRequestRequest(
    Guid? GroupId,
    Guid? BasinId = null,
    JobOrderType JobOrderType = JobOrderType.ClosedLine,
    ProductionPriority Priority = ProductionPriority.Normal,
    DateTime? OrderDate = null,
    string? Color = null,
    string? Notes = null,
    string? CustomerReference = null);
