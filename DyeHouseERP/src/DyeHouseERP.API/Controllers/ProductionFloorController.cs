using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Factory floor operational view (spec section 37): every LIVE Job Order with the stage it is
/// physically in right now, its elapsed time, and what came in and out of that stage.
/// It is a read-only operational feed - all stage work is still done on the Job Order itself.
/// </summary>
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
        [FromQuery] Guid? customerId, [FromQuery] Guid? itemId, [FromQuery] ProductionPriority? priority,
        [FromQuery] string? search)
        => Ok(await _mediator.Send(new GetProductionFloorQuery(
            stageDefinitionId, status, customerId, itemId, priority, search)));
}
