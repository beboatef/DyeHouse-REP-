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
    public FormationSpecificationsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationView)]
    [ProducesResponseType(typeof(List<FormationSpecTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FormationSpecTemplateDto>>> Get([FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetFormationSpecTemplatesQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    [ProducesResponseType(typeof(FormationSpecTemplateDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FormationSpecTemplateDto>> Create([FromBody] CreateFormationSpecTemplateCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    public async Task<ActionResult<FormationSpecTemplateDto>> Update(Guid id, [FromBody] UpdateFormationSpecTemplateCommand command)
    {
        if (id != command.Id) command = command with { Id = id };
        return Ok(await _mediator.Send(command));
    }

    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.FormationManageSpecifications)]
    public async Task<ActionResult<FormationSpecTemplateDto>> SetActive(Guid id, [FromBody] SetSpecificationActiveRequest request)
        => Ok(await _mediator.Send(new SetFormationSpecTemplateActiveCommand(id, request.IsActive)));
}

public record SetSpecificationActiveRequest(bool IsActive);
