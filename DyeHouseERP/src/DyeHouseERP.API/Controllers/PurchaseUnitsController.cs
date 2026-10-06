using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.PurchaseUnits.Commands;
using DyeHouseERP.Application.PurchaseUnits.DTOs;
using DyeHouseERP.Application.PurchaseUnits.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Purchase units of measure (spec section 44). The nine defaults are seeded,
/// additional bilingual units can be created here, and units are deactivated
/// rather than deleted - there is deliberately no DELETE endpoint, because a
/// unit that a purchase document already references must stay resolvable.
/// </summary>
[ApiController]
[Route("api/purchase-units")]
[Authorize]
public class PurchaseUnitsController : ControllerBase
{
    private readonly ISender _mediator;
    public PurchaseUnitsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<PurchaseUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PurchaseUnitDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetPurchaseUnitsQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseUnitDto>> Create([FromBody] CreatePurchaseUnitCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseUnitDto>> Update(Guid id, [FromBody] UpdatePurchaseUnitCommand command)
        => Ok(await _mediator.Send(command with { Id = id }));

    /// <summary>Activates or deactivates a unit. Deactivation is the delete substitute (spec section 44).</summary>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseUnitDto>> SetActive(Guid id, [FromBody] SetPurchaseUnitActiveRequest request)
        => Ok(await _mediator.Send(new SetPurchaseUnitActiveCommand(id, request.IsActive)));
}

public record SetPurchaseUnitActiveRequest(bool IsActive);
