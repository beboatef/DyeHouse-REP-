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
}

public record CancelDeliveryRequest(string Reason);
