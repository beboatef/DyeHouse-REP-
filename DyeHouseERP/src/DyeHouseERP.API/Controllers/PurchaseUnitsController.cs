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
    /// <summary>Initializes a new instance of the PurchaseUnitsController class.</summary>
    public PurchaseUnitsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists purchase units.</summary>
    /// <remarks>Route: GET api/purchase-units. Requires the 'purchases.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesView)]
    [ProducesResponseType(typeof(List<PurchaseUnitDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<PurchaseUnitDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetPurchaseUnitsQuery(activeOnly, search)));

    /// <summary>Creates purchase units.</summary>
    /// <remarks>Route: POST api/purchase-units. Requires the 'purchases.edit' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseUnitDto>> Create([FromBody] CreatePurchaseUnitCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Updates purchase units.</summary>
    /// <remarks>Route: PUT api/purchase-units/{id:guid}. Requires the 'purchases.edit' permission.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseUnitDto>> Update(Guid id, [FromBody] UpdatePurchaseUnitCommand command)
        => Ok(await _mediator.Send(command with { Id = id }));

    /// <summary>Activates or deactivates a unit. Deactivation is the delete substitute (spec section 44).</summary>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PurchasesEdit)]
    [ProducesResponseType(typeof(PurchaseUnitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PurchaseUnitDto>> SetActive(Guid id, [FromBody] SetPurchaseUnitActiveRequest request)
        => Ok(await _mediator.Send(new SetPurchaseUnitActiveCommand(id, request.IsActive)));
}

/// <summary>Request payload to set purchase unit active.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetPurchaseUnitActiveRequest(bool IsActive);
