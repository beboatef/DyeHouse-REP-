using DyeHouseERP.Application.CustomerPortal.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Production floor dashboard feed (spec section 37).</summary>
[ApiController]
[Route("api/production-floor")]
[Authorize]
public class ProductionFloorController : ControllerBase
{
    private readonly ISender _mediator;
    public ProductionFloorController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<ProductionFloorRowDto>>> Get(
        [FromQuery] Guid? stageDefinitionId, [FromQuery] ProductionOrderStatus? status,
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] ProductionPriority? priority)
        => Ok(await _mediator.Send(new GetProductionFloorQuery(stageDefinitionId, status, customerId, itemId, priority)));
}
