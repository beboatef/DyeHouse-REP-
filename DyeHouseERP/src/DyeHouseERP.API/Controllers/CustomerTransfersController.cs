using DyeHouseERP.Application.CustomerTransfers.Commands;
using DyeHouseERP.Application.CustomerTransfers.DTOs;
using DyeHouseERP.Application.CustomerTransfers.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Customer-to-customer raw material ownership transfer (spec section 15).</summary>
[ApiController]
[Route("api/customer-transfers")]
[Authorize]
public class CustomerTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    public CustomerTransfersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawTransfer)]
    [ProducesResponseType(typeof(List<CustomerTransferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerTransferDto>>> Get([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetCustomerTransfersQuery(customerId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawTransfer)]
    [ProducesResponseType(typeof(CustomerTransferDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerTransferDto>> Create([FromBody] CreateCustomerTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
