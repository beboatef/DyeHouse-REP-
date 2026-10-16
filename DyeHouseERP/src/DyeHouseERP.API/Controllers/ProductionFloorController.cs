using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Factory floor operational view (spec section 37): every Job Order in the requested slice, with the
/// stage it is physically in right now, its elapsed time, and what came in and out of that stage.
/// It is a read-only operational feed - all stage work is still done on the Job Order itself.
///
/// The <c>view</c> parameter is which slice the supervisor is looking at: <c>Open</c> (the default -
/// jobs still on the floor), <c>Delivered</c> (jobs already handed to the customer), <c>Closed</c>
/// (completed or cancelled without a delivery) or <c>All</c>. A delivered job never appears in the
/// ordinary view; it is not work waiting to be done, and leaving it there would have the floor
/// chasing jobs that have already left the building. An explicit <c>status</c> filter wins over the
/// slice, because asking for completed jobs is a deliberate choice.
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
        [FromQuery] string? search, [FromQuery] ProductionFloorView view = ProductionFloorView.Open)
        => Ok(await _mediator.Send(new GetProductionFloorQuery(
            stageDefinitionId, status, customerId, itemId, priority, search, view)));
}
