using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.API.Common;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/deliveries")]
[Authorize]
public class DeliveriesController : ControllerBase
{
    private readonly ISender _mediator;
    public DeliveriesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<List<DeliveryDto>>> Get([FromQuery] Guid? customerId, [FromQuery] DeliveryStatus? status)
        => Ok(await _mediator.Send(new GetDeliveriesQuery(customerId, status)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<DeliveryDto>> GetById(Guid id) => Ok(await _mediator.Send(new GetDeliveryByIdQuery(id)));

    /// <summary>Printable delivery document PDF (spec section 39) - has no financial value, just the physical delivery record.</summary>
    /// <summary>Deliveries register as Excel or PDF - one row per delivered line, with raw origin (spec section 31).</summary>
    [HttpGet("export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] Guid? customerId, [FromQuery] DeliveryStatus? status,
        [FromQuery] string format = "excel", [FromServices] IReportExportService export = null!)
    {
        var deliveries = await _mediator.Send(new GetDeliveriesQuery(customerId, status));

        var headers = new[] { "Delivery no.", "Date", "Customer", "Job order", "Item", "Color", "Qty KG", "Qty M", "Pieces", "Raw origin", "Status" };
        var rows = deliveries.SelectMany(d => d.Lines.Select(l => new object?[]
        {
            d.DeliveryNumber, d.DeliveryDate.ToString("yyyy-MM-dd"), $"{d.CustomerCode} - {d.CustomerName}",
            l.ProductionOrderNumber, $"{l.ItemCode}", l.Color, l.QuantityKg, l.QuantityMeter, l.PieceCount,
            l.RawOrigin, d.Status.ToString()
        })).ToList();

        return ExportFileHelper.ToFile(export, format, "Deliveries", "DyeHouse ERP", headers, rows, "deliveries", "Deliveries");
    }

    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetDeliveryPdf(Guid id, [FromServices] DyeHouseERP.Application.Common.Interfaces.IReportExportService export)
    {
        var delivery = await _mediator.Send(new GetDeliveryByIdQuery(id));

        var headers = new List<string> { "Production Order", "Item", "Color", "Qty KG", "Qty Meter", "Pieces" };
        var rows = delivery.Lines.Select(l => new object?[]
        {
            l.ProductionOrderNumber, l.ItemCode, l.Color, l.QuantityKg, l.QuantityMeter, l.PieceCount
        }).ToList();

        var subtitle = $"{delivery.CustomerCode} - {delivery.CustomerName}  |  {delivery.DeliveryDate:yyyy-MM-dd}  |  {delivery.Status}";
        var bytes = export.GeneratePdf($"إذن تسليم رقم {delivery.DeliveryNumber}", subtitle, headers, rows);
        return File(bytes, "application/pdf", $"delivery-{delivery.DeliveryNumber}.pdf");
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyDeliver)]
    public async Task<ActionResult<DeliveryDto>> Create([FromBody] CreateDeliveryCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/prepare")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyDeliver)]
    public async Task<ActionResult<DeliveryDto>> Prepare(Guid id) => Ok(await _mediator.Send(new MarkDeliveryPreparedCommand(id)));

    [HttpPost("{id:guid}/deliver")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyDeliver)]
    public async Task<ActionResult<DeliveryDto>> Deliver(Guid id) => Ok(await _mediator.Send(new MarkDeliveryDeliveredCommand(id)));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyDeliver)]
    public async Task<ActionResult<DeliveryDto>> Cancel(Guid id, [FromBody] CancelDeliveryRequest request)
        => Ok(await _mediator.Send(new CancelDeliveryCommand(id, request.Reason)));

    /// <summary>
    /// Corrects an ALREADY APPROVED delivery (spec section 30). The original
    /// posted stock movements are never touched: the difference is posted as a
    /// compensating movement (a reduction returns stock to Ready Goods, an
    /// increase draws it out) and every changed line is written to the audit log
    /// with its before/after figures, user, time and reason.
    /// </summary>
    [HttpPost("{id:guid}/lines/corrections")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyEditPostApproval)]
    [ProducesResponseType(typeof(DeliveryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeliveryDto>> CorrectApprovedLines(Guid id, [FromBody] EditDeliveredDeliveryRequest request)
        => Ok(await _mediator.Send(new EditDeliveredDeliveryCommand(
            id, request.Lines.Select(l => new DeliveryLineQuantity(l.LineId, l.QuantityKg, l.QuantityMeter)).ToList(),
            request.Reason)));
}

public record CancelDeliveryRequest(string Reason);

/// <summary>Request body for correcting an approved delivery's line quantities.</summary>
public record EditDeliveredDeliveryLineRequest(Guid LineId, decimal? QuantityKg, decimal? QuantityMeter);

public record EditDeliveredDeliveryRequest(IReadOnlyList<EditDeliveredDeliveryLineRequest> Lines, string Reason);
