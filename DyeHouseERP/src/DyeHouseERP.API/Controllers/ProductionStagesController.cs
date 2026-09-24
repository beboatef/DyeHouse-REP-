using DyeHouseERP.Application.ProductionStages.Commands;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Application.ProductionStages.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

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
    public async Task<ActionResult<List<ProductionStageDefinitionDto>>> Get([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetProductionStageDefinitionsQuery(activeOnly)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ProductionStageDefinitionDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductionStageDefinitionDto>> Create([FromBody] CreateProductionStageDefinitionCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
