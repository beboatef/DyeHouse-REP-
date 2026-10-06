using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.CustomerReturns.DTOs;
using DyeHouseERP.Application.CustomerReturns.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Customer returns of processed goods (spec sections 32-33). Recording a return
/// posts IN ledger rows into the RAW MATERIAL warehouse, so it is gated by the
/// ordinary inventory permissions rather than by an approval workflow - there is
/// deliberately no inspection/approval step here.
/// </summary>
[ApiController]
[Route("api/customer-returns")]
[Authorize]
public class CustomerReturnsController : ControllerBase
{
    private readonly ISender _mediator;
    public CustomerReturnsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<CustomerReturnDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerReturnDto>>> Get(
        [FromQuery] Guid? customerId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetCustomerReturnsQuery(customerId, fromDate, toDate, search)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerReturnDto>> GetById(Guid id)
        => Ok(await _mediator.Send(new GetCustomerReturnByIdQuery(id)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryEdit)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerReturnDto>> Create([FromBody] CreateCustomerReturnCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
