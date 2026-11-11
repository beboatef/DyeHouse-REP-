using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Application.ReadyGoods.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Hosts the ready goods endpoints.</summary>
[ApiController]
[Route("api/ready-goods")]
[Authorize]
public class ReadyGoodsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the ReadyGoodsController class.</summary>
    public ReadyGoodsController(ISender mediator) => _mediator = mediator;

    /// <summary>Gets transfers.</summary>
    /// <remarks>Route: GET api/ready-goods/transfers. Requires the 'ready.view' permission.</remarks>
    [HttpGet("transfers")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<List<ReadyGoodsTransferDto>>> GetTransfers() => Ok(await _mediator.Send(new GetReadyGoodsTransfersQuery()));

    /// <summary>Ready-goods transfers (the "ترحيل" register) as Excel or PDF (spec section 29).</summary>
    [HttpGet("transfers/export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportTransfers([FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var transfers = await _mediator.Send(new GetReadyGoodsTransfersQuery());

        var headers = new[] { "Transfer no.", "Date", "Job order", "Customer", "Item", "Color", "Category", "Qty KG", "Qty M", "Pieces" };
        var rows = transfers.Select(t => new object?[]
        {
            t.TransferNumber, t.TransferDate.ToString("yyyy-MM-dd"), t.ProductionOrderNumber,
            $"{t.CustomerCode}", $"{t.ItemCode}", t.Color, t.Category, t.QuantityKg, t.QuantityMeter, t.PieceCount
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Ready Goods Transfers", null, headers, rows, "ready-goods-transfers", "Transfers");
    }

    /// <summary>
    /// Live ready-goods balance as Excel or PDF (spec section 30). Grouped by Job
    /// Order - the same traceable view the screen shows, never a flat item total.
    /// </summary>
    [HttpGet("balance/export")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportBalance([FromQuery] Guid? customerId, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var balances = await _mediator.Send(new GetReadyGoodsBalanceQuery(customerId));

        var headers = new[] { "Job order", "Customer", "Item", "Color", "Category", "Ready KG", "Ready M" };
        var rows = balances.Select(b => new object?[]
        {
            b.ProductionOrderNumber, $"{b.CustomerCode} - {b.CustomerName}", $"{b.ItemCode} - {b.ItemName}",
            b.Color, b.Category, b.RemainingKg, b.RemainingMeter
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Ready Goods Balance", null, headers, rows, "ready-goods-balance", "Balance");
    }

    /// <summary>Gets balance.</summary>
    /// <remarks>Route: GET api/ready-goods/balance. Requires the 'ready.view' permission.</remarks>
    [HttpGet("balance")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<List<ReadyGoodsBalanceDto>>> GetBalance([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetReadyGoodsBalanceQuery(customerId)));

    /// <summary>Creates transfer.</summary>
    /// <remarks>Route: POST api/ready-goods/transfers. Requires the 'ready.transfer' permission.</remarks>
    [HttpPost("transfers")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyTransfer)]
    public async Task<ActionResult<ReadyGoodsTransferDto>> CreateTransfer([FromBody] CreateReadyGoodsTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetTransfers), new { }, result);
    }

    /// <summary>
    /// The register of ready-goods-to-raw movements (spec section 29) - every authorized, reasoned move
    /// of finished stock back into raw material stock.
    /// </summary>
    [HttpGet("raw-returns")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    [ProducesResponseType(typeof(List<ReadyGoodsRawReturnDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<ReadyGoodsRawReturnDto>>> GetRawReturns([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetReadyGoodsRawReturnsQuery(customerId)));

    /// <summary>
    /// Moves ready goods BACK into raw material stock for rework or reclassification (spec section 29).
    /// It is the only way stock leaves Ready Goods for the raw side - nothing does it implicitly - so it
    /// carries its own permission, a mandatory reason and an audit entry, and it posts its two ledger
    /// rows (out of Ready Goods, into the raw lot) exactly once.
    /// </summary>
    [HttpPost("raw-returns")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyReturnToRaw)]
    [ProducesResponseType(typeof(ReadyGoodsRawReturnDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReadyGoodsRawReturnDto>> ReturnToRaw([FromBody] ReturnReadyGoodsToRawStockCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRawReturns), new { }, result);
    }
    /// <summary>Updates transfer.</summary>
    /// <remarks>Route: PUT api/ready-goods/transfers/{id:guid}. Requires the 'inventory.edit' permission.</remarks>
    [HttpPut("transfers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryEdit)]
    public async Task<ActionResult<ReadyGoodsTransferDto>> UpdateTransfer(
        Guid id,
        [FromBody] UpdateReadyGoodsTransferRequest request)
    {
        // Quantities are deliberately absent: a posted transfer's stock effect
        // is in the append-only ledger and can only be corrected by cancelling
        // (which reverses) - see DeleteReadyGoodsTransferCommand.
        var command = new UpdateReadyGoodsTransferCommand(
            id,
            request.TransferDate,
            request.PieceCount,
            request.Notes);

        return Ok(await _mediator.Send(command));
    }

    /// <summary>
    /// Cancels a posted transfer and reverses its ledger rows. The endpoint
    /// keeps the DELETE shape for compatibility, but nothing is physically
    /// removed - the transfer stays listed with status Cancelled.
    /// </summary>
    [HttpDelete("transfers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryDelete)]
    public async Task<IActionResult> DeleteTransfer(Guid id, [FromQuery] string reason)
    {
        await _mediator.Send(new DeleteReadyGoodsTransferCommand(id, reason));
        return NoContent();
    }

}

/// <summary>Request payload to update ready goods transfer.</summary>
/// <param name="TransferDate">The transfer date.</param>
/// <param name="PieceCount">The piece count.</param>
/// <param name="Notes">The notes.</param>
public sealed record UpdateReadyGoodsTransferRequest(
    DateTime TransferDate,
    int? PieceCount,
    string? Notes);
