using DyeHouseERP.Application.ProductionStages.Commands;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Application.ProductionStages.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Body of the activate/deactivate request (spec section 19).</summary>
public record SetStageActiveRequest(bool IsActive);

/// <summary>
/// Admin configuration for the production stage engine (spec section 19).
/// There is no fixed list of stage names anywhere in the system - whatever
/// is created here, in Sequence order, becomes every new Production Order's
/// route.
/// </summary>
[ApiController]
[Route("api/production-stages")]
[Authorize]
public class ProductionStagesController : ControllerBase
{
    private readonly ISender _mediator;
    public ProductionStagesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<ProductionStageDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProductionStageDefinitionDto>>> Get(
        [FromQuery] bool? activeOnly, [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetProductionStageDefinitionsQuery(activeOnly, search)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> Create([FromBody] CreateProductionStageDefinitionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    /// <summary>
    /// Designates this stage as التشكيل - the stage every new Job Order starts at
    /// (spec sections 12 and 54). Exactly one stage holds the flag; setting it here
    /// clears it from whichever stage held it before.
    /// </summary>
    [HttpPost("{id:guid}/formation-stage")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> SetFormationStage(Guid id)
        => Ok(await _mediator.Send(new SetFormationStageCommand(id)));

    /// <summary>
    /// Designates this stage as الجاهز - the FINAL stage (spec section 17). Exactly
    /// one stage holds the flag, it is never inferred from Sequence, and the stage
    /// that is already التشكيل is refused so a route cannot start and end at the
    /// same stop.
    /// </summary>
    [HttpPost("{id:guid}/ready-goods-stage")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> SetReadyGoodsStage(Guid id)
        => Ok(await _mediator.Send(new SetReadyGoodsStageCommand(id)));

    /// <summary>
    /// Edits the display name, the notes and the order of an existing stage (spec section 19).
    /// Reordering is a display concern only, and the stage code is its stable identity, so
    /// neither changing the order nor renaming touches a Job Order's route or history.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> Update(
        Guid id, [FromBody] UpdateProductionStageDefinitionCommand command)
        => Ok(await _mediator.Send(command with { StageDefinitionId = id }));

    /// <summary>
    /// Activates or deactivates a stage (spec section 19). Deactivation never deletes: the
    /// row, the machine links and every past execution stay, and the stage simply stops being
    /// offered for new production. The formation stage and the ready-goods stage cannot be
    /// deactivated - that role has to be moved to another stage first.
    /// </summary>
    [HttpPost("{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> SetActive(Guid id, [FromBody] SetStageActiveRequest request)
        => Ok(await _mediator.Send(new SetProductionStageActiveCommand(id, request.IsActive)));
}
