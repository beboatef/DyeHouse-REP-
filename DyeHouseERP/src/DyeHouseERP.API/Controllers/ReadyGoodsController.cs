using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Application.ReadyGoods.Queries;
using DyeHouseERP.API.Authorization;
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
