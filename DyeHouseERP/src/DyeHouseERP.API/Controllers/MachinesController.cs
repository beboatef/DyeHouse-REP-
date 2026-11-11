using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.ProductionMasterData.Commands;
using DyeHouseERP.Application.ProductionMasterData.DTOs;
using DyeHouseERP.Application.ProductionMasterData.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Dynamic MACHINE master data (S-Dynamic-Machines) and its stage mapping.
/// There is no fixed machine list and no maximum - machines are created,
/// renamed, deactivated and re-mapped here, never in code.
/// </summary>
[ApiController]
[Route("api/machines")]
[Authorize]
public class MachinesController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the MachinesController class.</summary>
    public MachinesController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists machines.</summary>
    /// <remarks>Route: GET api/machines. Requires the 'production.view' permission.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<MachineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<MachineDto>>> Get(
        [FromQuery] bool? activeOnly, [FromQuery] Guid? stageDefinitionId)
        => Ok(await _mediator.Send(new GetMachinesQuery(activeOnly, stageDefinitionId)));

    /// <summary>All stage→machine links (a machine may serve several stages).</summary>
    [HttpGet("stage-links")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<StageMachineLinkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<StageMachineLinkDto>>> GetStageLinks([FromQuery] Guid? stageDefinitionId)
        => Ok(await _mediator.Send(new GetStageMachineLinksQuery(stageDefinitionId)));

    /// <summary>Creates machines.</summary>
    /// <remarks>Route: POST api/machines. Requires the 'production.manage_machines' permission.</remarks>
    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MachineDto>> Create([FromBody] CreateMachineCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>Updates machines.</summary>
    /// <remarks>Route: PUT api/machines/{id:guid}. Requires the 'production.manage_machines' permission.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MachineDto>> Update(Guid id, [FromBody] UpdateMachineRequest request)
        => Ok(await _mediator.Send(new UpdateMachineCommand(id, request.Name, request.Notes)));

    /// <summary>Activates/deactivates a machine. A deactivated machine is not used for new execution but keeps its history.</summary>
    [HttpPost("{id:guid}/activation")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MachineDto>> SetActivation(Guid id, [FromBody] SetActivationRequest request)
        => Ok(await _mediator.Send(new SetMachineActivationCommand(id, request.IsActive)));

    /// <summary>Replaces the set of stages this machine is mapped to.</summary>
    [HttpPut("{id:guid}/stages")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MachineDto>> SetStages(Guid id, [FromBody] SetMachineStagesRequest request)
        => Ok(await _mediator.Send(new SetMachineStagesCommand(id, request.StageDefinitionIds ?? new List<Guid>())));
}

/// <summary>Request payload to update machine.</summary>
/// <param name="Name">The name.</param>
/// <param name="Notes">The notes.</param>
public record UpdateMachineRequest(string Name, string? Notes);
/// <summary>Request payload to set machine stages.</summary>
/// <param name="StageDefinitionIds">The stage definition ids.</param>
public record SetMachineStagesRequest(List<Guid>? StageDefinitionIds);

/// <summary>Shared activate/deactivate payload for machine and production-shift master data.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetActivationRequest(bool IsActive);
