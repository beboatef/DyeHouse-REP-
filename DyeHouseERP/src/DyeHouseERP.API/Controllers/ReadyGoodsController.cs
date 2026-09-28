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

[ApiController]
[Route("api/ready-goods")]
[Authorize]
public class ReadyGoodsController : ControllerBase
{
    private readonly ISender _mediator;
    public ReadyGoodsController(ISender mediator) => _mediator = mediator;

    [HttpGet("transfers")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<List<ReadyGoodsTransferDto>>> GetTransfers() => Ok(await _mediator.Send(new GetReadyGoodsTransfersQuery()));

    /// <summary>Ready-goods transfers (the "ترحيل" register) as Excel or PDF (spec section 29).</summary>
    [HttpGet("transfers/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportTransfers([FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var transfers = await _mediator.Send(new GetReadyGoodsTransfersQuery());

        var headers = new[] { "Transfer no.", "Date", "Job order", "Customer", "Item", "Color", "Qty KG", "Qty M", "Pieces" };
        var rows = transfers.Select(t => new object?[]
        {
            t.TransferNumber, t.TransferDate.ToString("yyyy-MM-dd"), t.ProductionOrderNumber,
            $"{t.CustomerCode}", $"{t.ItemCode}", t.Color, t.QuantityKg, t.QuantityMeter, t.PieceCount
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Ready Goods Transfers", "DyeHouse ERP", headers, rows, "ready-goods-transfers", "Transfers");
    }

    /// <summary>
    /// Live ready-goods balance as Excel or PDF (spec section 30). Grouped by Job
    /// Order - the same traceable view the screen shows, never a flat item total.
    /// </summary>
    [HttpGet("balance/export")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReportsExport)]
    public async Task<IActionResult> ExportBalance([FromQuery] Guid? customerId, [FromQuery] string format = "excel",
        [FromServices] IReportExportService export = null!)
    {
        var balances = await _mediator.Send(new GetReadyGoodsBalanceQuery(customerId));

        var headers = new[] { "Job order", "Customer", "Item", "Color", "Ready KG", "Ready M" };
        var rows = balances.Select(b => new object?[]
        {
            b.ProductionOrderNumber, $"{b.CustomerCode} - {b.CustomerName}", $"{b.ItemCode} - {b.ItemName}",
            b.Color, b.RemainingKg, b.RemainingMeter
        }).ToList();

        return ExportFileHelper.ToFile(export, format, "Ready Goods Balance", "DyeHouse ERP", headers, rows, "ready-goods-balance", "Balance");
    }

    [HttpGet("balance")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyView)]
    public async Task<ActionResult<List<ReadyGoodsBalanceDto>>> GetBalance([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetReadyGoodsBalanceQuery(customerId)));

    [HttpPost("transfers")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ReadyTransfer)]
    public async Task<ActionResult<ReadyGoodsTransferDto>> CreateTransfer([FromBody] CreateReadyGoodsTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetTransfers), new { }, result);
    }
    [HttpPut("transfers/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryEdit)]
    public async Task<ActionResult<ReadyGoodsTransferDto>> UpdateTransfer(
        Guid id,
        [FromBody] UpdateReadyGoodsTransferRequest request)
    {
        var command = new UpdateReadyGoodsTransferCommand(
            id,
            request.TransferDate,
            request.QuantityKg,
            request.QuantityMeter,
            request.PieceCount,
            request.Notes);

        return Ok(await _mediator.Send(command));
    }

    [HttpDelete("transfers/{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryDelete)]
    public async Task<IActionResult> DeleteTransfer(Guid id)
    {
        await _mediator.Send(new DeleteReadyGoodsTransferCommand(id));
        return NoContent();
    }

}

public sealed record UpdateReadyGoodsTransferRequest(
    DateTime TransferDate,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    int? PieceCount,
    string? Notes);
