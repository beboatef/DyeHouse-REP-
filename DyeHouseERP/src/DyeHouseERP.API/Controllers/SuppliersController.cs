using DyeHouseERP.Application.Suppliers.Commands;
using DyeHouseERP.Application.Suppliers.DTOs;
using DyeHouseERP.Application.Suppliers.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Supplier master (spec section 35). Required by the checks register (outgoing
/// supplier checks and endorsements) and by the purchases module that builds on
/// it next: code is manually entered and unique, phone/address stay optional.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ISender _mediator;
    public SuppliersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersView)]
    [ProducesResponseType(typeof(List<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetSuppliersQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersCreate)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] CreateSupplierCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SuppliersEdit)]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] UpdateSupplierCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }
}
