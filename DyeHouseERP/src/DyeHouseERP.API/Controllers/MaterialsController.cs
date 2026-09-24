using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.Materials.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/materials")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsView)]
    public async Task<ActionResult<List<MaterialDto>>> Get([FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetMaterialsQuery(activeOnly)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ItemsCreate)]
    public async Task<ActionResult<MaterialDto>> Create([FromBody] CreateMaterialCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}

[ApiController]
[Route("api/material-transfers")]
[Authorize]
public class MaterialTransfersController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialTransfersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryView)]
    public async Task<ActionResult<List<MaterialTransferDto>>> Get() => Ok(await _mediator.Send(new GetMaterialTransfersQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.InventoryAdjust)]
    public async Task<ActionResult<MaterialTransferDto>> Create([FromBody] CreateMaterialTransferCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}

[ApiController]
[Route("api/material-issues")]
[Authorize]
public class MaterialIssuesController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialIssuesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<MaterialIssueDto>>> Get([FromQuery] Guid? productionOrderId)
        => Ok(await _mediator.Send(new GetMaterialIssuesQuery(productionOrderId)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    public async Task<ActionResult<MaterialIssueDto>> Create([FromBody] CreateMaterialIssueCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}

[ApiController]
[Route("api/material-preparations")]
[Authorize]
public class MaterialPreparationsController : ControllerBase
{
    private readonly ISender _mediator;
    public MaterialPreparationsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<MaterialPreparationDto>>> Get() => Ok(await _mediator.Send(new GetMaterialPreparationsQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionExecuteStage)]
    public async Task<ActionResult<MaterialPreparationDto>> Create([FromBody] CreateMaterialPreparationCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }
}
