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
    public MachinesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<MachineDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MachineDto>>> Get(
        [FromQuery] bool? activeOnly, [FromQuery] Guid? stageDefinitionId)
        => Ok(await _mediator.Send(new GetMachinesQuery(activeOnly, stageDefinitionId)));

    /// <summary>All stage→machine links (a machine may serve several stages).</summary>
    [HttpGet("stage-links")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<StageMachineLinkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StageMachineLinkDto>>> GetStageLinks([FromQuery] Guid? stageDefinitionId)
        => Ok(await _mediator.Send(new GetStageMachineLinksQuery(stageDefinitionId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MachineDto>> Create([FromBody] CreateMachineCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MachineDto>> Update(Guid id, [FromBody] UpdateMachineRequest request)
        => Ok(await _mediator.Send(new UpdateMachineCommand(id, request.Name, request.Notes)));

    /// <summary>Activates/deactivates a machine. A deactivated machine is not used for new execution but keeps its history.</summary>
    [HttpPost("{id:guid}/activation")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MachineDto>> SetActivation(Guid id, [FromBody] SetActivationRequest request)
        => Ok(await _mediator.Send(new SetMachineActivationCommand(id, request.IsActive)));

    /// <summary>Replaces the set of stages this machine is mapped to.</summary>
    [HttpPut("{id:guid}/stages")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionManageMachines)]
    [ProducesResponseType(typeof(MachineDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MachineDto>> SetStages(Guid id, [FromBody] SetMachineStagesRequest request)
        => Ok(await _mediator.Send(new SetMachineStagesCommand(id, request.StageDefinitionIds ?? new List<Guid>())));
}

public record UpdateMachineRequest(string Name, string? Notes);
public record SetMachineStagesRequest(List<Guid>? StageDefinitionIds);

/// <summary>Shared activate/deactivate payload for machine and production-shift master data.</summary>
public record SetActivationRequest(bool IsActive);
