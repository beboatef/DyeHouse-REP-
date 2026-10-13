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

    /// <summary>
    /// Cancels a return entered in error - the wrong lot, quantity or customer (spec sections 32-33 + 49).
    /// A return posts stock the moment it is created, so it is never edited or deleted: the document and
    /// its IN rows are preserved and the stock it restored is taken back out with an equal-and-opposite
    /// OUT row, recorded with the reason and the user. Refused while that material has already been used.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RawReturnCancel)]
    [ProducesResponseType(typeof(CustomerReturnDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerReturnDto>> Cancel(Guid id, [FromBody] CancelCustomerReturnRequest request)
        => Ok(await _mediator.Send(new CancelCustomerReturnCommand(id, request.Reason)));
}

/// <summary>
/// A cancellation always carries a reason: it is stored on the return and written to the audit
/// trail, so a corrected mistake is explainable later.
/// </summary>
public record CancelCustomerReturnRequest(string Reason);
