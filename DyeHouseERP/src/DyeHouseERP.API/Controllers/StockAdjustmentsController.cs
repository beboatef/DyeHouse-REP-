using DyeHouseERP.Application.StockAdjustments.Commands;
using DyeHouseERP.Application.StockAdjustments.DTOs;
using DyeHouseERP.Application.StockAdjustments.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/stock-adjustments")]
[Authorize]
public class StockAdjustmentsController : ControllerBase
{
    private readonly ISender _mediator;
    public StockAdjustmentsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<StockAdjustmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StockAdjustmentDto>>> Get([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetStockAdjustmentsQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(StockAdjustmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<StockAdjustmentDto>> Create([FromBody] CreateStockAdjustmentCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
