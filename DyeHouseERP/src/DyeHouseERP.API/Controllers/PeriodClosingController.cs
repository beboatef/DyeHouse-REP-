using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.PeriodClosing.Commands;
using DyeHouseERP.Application.PeriodClosing.DTOs;
using DyeHouseERP.Application.PeriodClosing.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Period closing (spec section 43). Access is decided by the granular settings.manage permission -
/// closing and reopening a period is a significant audited action, so the right is granted explicitly
/// rather than bundled with the admin role. Authentication is always required.
/// </summary>
[ApiController]
[Route("api/period-closes")]
[Authorize]
public class PeriodClosingController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the PeriodClosingController class.</summary>
    public PeriodClosingController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists period closing.</summary>
    /// <remarks>Route: GET api/period-closes. Requires the 'settings.manage' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<List<PeriodCloseDto>>> Get() => Ok(await _mediator.Send(new GetPeriodClosesQuery()));

    /// <summary>Closes period closing.</summary>
    /// <remarks>Route: POST api/period-closes. Requires the 'settings.manage' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<PeriodCloseDto>> Close([FromBody] ClosePeriodCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Reopens period closing.</summary>
    /// <remarks>Route: POST api/period-closes/{id:guid}/reopen. Requires the 'settings.manage' permission.</remarks>
    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<PeriodCloseDto>> Reopen(Guid id, [FromBody] ReopenPeriodRequest request)
        => Ok(await _mediator.Send(new ReopenPeriodCommand(id, request.Reason)));
}

/// <summary>Request payload to reopen period.</summary>
/// <param name="Reason">The reason.</param>
public record ReopenPeriodRequest(string Reason);
