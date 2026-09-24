using DyeHouseERP.Application.RawReceipts.Commands;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Application.RawReceipts.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/raw-messages")]
[Authorize]
public class RawMessagesController : ControllerBase
{
    private readonly ISender _mediator;
    public RawMessagesController(ISender mediator) => _mediator = mediator;

    /// <summary>
    /// List raw receipt messages with their ledger-derived remaining balance
    /// per line. This is what feeds the manual "choose message(s) to
    /// allocate from" picker (spec section 4 - explicitly NO FIFO).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReceive)]
    [ProducesResponseType(typeof(List<RawMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RawMessageDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] bool onlyWithBalance = false)
        => Ok(await _mediator.Send(new GetRawMessagesQuery(customerId, itemId, onlyWithBalance)));

    /// <summary>Single-message lookup - feeds the print-preview screen and the QR scan view (spec sections 39-40).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReceive)]
    public async Task<ActionResult<RawMessageDto>> GetById(Guid id) => Ok(await _mediator.Send(new GetRawMessageByIdQuery(id)));

    /// <summary>
    /// Create a raw receipt message. The message number is always
    /// system-generated (spec section 6) - never accepted from the client.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReceive)]
    [ProducesResponseType(typeof(RawMessageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RawMessageDto>> Create([FromBody] CreateRawMessageCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Record the receiving inspection result for a message (spec section 11).</summary>
    [HttpPost("{id:guid}/inspection")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawInspect)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordInspection(Guid id, [FromBody] RecordInspectionRequest request)
    {
        await _mediator.Send(new RecordInspectionCommand(id, request.Result, request.Notes));
        return NoContent();
    }

    /// <summary>Printable raw receipt/message PDF (spec section 39) - the central traceability document for this batch of customer-owned fabric.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> GetPdf(Guid id, [FromServices] DyeHouseERP.Application.Common.Interfaces.IReportExportService export)
    {
        var message = await _mediator.Send(new GetRawMessageByIdQuery(id));

        var headers = new List<string> { "Item", "Qty KG", "Qty Meter", "Remaining KG", "Remaining Meter", "Pieces" };
        var rows = message.Lines.Select(l => new object?[]
        {
            $"{l.ItemCode} - {l.ItemName}", l.QuantityKg, l.QuantityMeter, l.RemainingKg, l.RemainingMeter, l.PieceCount
        }).ToList();

        var subtitle = $"{message.CustomerCode} - {message.CustomerName}  |  {message.WarehouseName}  |  {message.ReceiptDate:yyyy-MM-dd}  |  {message.InspectionStatus}";
        var bytes = export.GeneratePdf($"إذن استلام خام رقم {message.MessageNumber}", subtitle, headers, rows);
        return File(bytes, "application/pdf", $"raw-message-{message.MessageNumber}.pdf");
    }
}

public record RecordInspectionRequest(Domain.Enums.InspectionStatus Result, string? Notes);
