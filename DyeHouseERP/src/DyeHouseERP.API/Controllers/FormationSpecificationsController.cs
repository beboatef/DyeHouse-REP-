using DyeHouseERP.Application.FormationRequests.Commands;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Application.FormationRequests.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Reusable formation specification master ("cells", spec section 29): width,
/// meter-per-kg or g/m², tub/tube format, winding tape format and the four
/// instruction blocks, saved once and selected on every future request.
/// Editing a template never rewrites a request that already used it - each
/// group keeps its own snapshot.
/// </summary>
[ApiController]
[Route("api/formation-specifications")]
[Authorize]
public class FormationSpecificationsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the FormationSpecificationsController class.</summary>
    public FormationSpecificationsController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists formation specifications.</summary>
    /// <remarks>Route: GET api/formation-specifications. Requires the 'formation.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationView)]
    [ProducesResponseType(typeof(List<FormationSpecTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<FormationSpecTemplateDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetFormationSpecTemplatesQuery(activeOnly, search)));

    /// <summary>Creates formation specifications.</summary>
    /// <remarks>Route: POST api/formation-specifications. Requires the 'formation.manage_specifications' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    [ProducesResponseType(typeof(FormationSpecTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FormationSpecTemplateDto>> Create([FromBody] CreateFormationSpecTemplateCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Updates formation specifications.</summary>
    /// <remarks>Route: PUT api/formation-specifications/{id:guid}. Requires the 'formation.manage_specifications' permission.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    public async Task<ActionResult<FormationSpecTemplateDto>> Update(Guid id, [FromBody] UpdateFormationSpecTemplateCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    /// <summary>Sets active.</summary>
    /// <remarks>Route: POST api/formation-specifications/{id:guid}/active. Requires the 'formation.manage_specifications' permission.</remarks>
    [HttpPost("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    public async Task<ActionResult<FormationSpecTemplateDto>> SetActive(Guid id, [FromBody] SetSpecificationActiveRequest request)
        => Ok(await _mediator.Send(new SetFormationSpecTemplateActiveCommand(id, request.IsActive)));
}

/// <summary>Request payload to set specification active.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetSpecificationActiveRequest(bool IsActive);
