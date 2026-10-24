using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.Separates.Commands;
using DyeHouseERP.Application.Separates.DTOs;
using DyeHouseERP.Application.Separates.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Reprocessable material tracked separately from loss/waste (spec sections 22-23).</summary>
[ApiController]
[Route("api/separates")]
[Authorize]
public class SeparatesController : ControllerBase
{
    private readonly ISender _mediator;
    public SeparatesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    [ProducesResponseType(typeof(List<SeparateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SeparateDto>>> Get([FromQuery] SeparateStatus? status)
        => Ok(await _mediator.Send(new GetSeparatesQuery(status)));

    /// <summary>
    /// Creates a reprocessing Job Order from one separate. The material must already be posted
    /// to Ready Goods; the handler posts the linked stock OUT and order atomically. <c>startingStageId</c>
    /// is optional and must identify an existing, active stage.
    /// </summary>
    [HttpPost("{id:guid}/reprocess")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionReprocess)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Reprocess(Guid id, [FromBody] ReprocessSeparateRequest request)
        => Ok(await _mediator.Send(new ReprocessSeparateCommand(id, request.OrderDate, request.Notes, request.StartingStageId)));

    /// <summary>Creates one reprocessing order from several posted separates of the same customer and item.</summary>
    [HttpPost("reprocess-batch")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionReprocess)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> ReprocessBatch([FromBody] ReprocessSeparatesBatchRequest request)
    {
        if (request.SeparateIds is null || request.SeparateIds.Count == 0)
            return BadRequest("Select at least one separate.");
        return Ok(await _mediator.Send(new ReprocessSeparateCommand(
            request.SeparateIds[0], request.OrderDate, request.Notes, request.StartingStageId,
            request.SeparateIds.Skip(1).ToArray())));
    }

    [HttpPost("{id:guid}/scrap")]
    // Its own right (decided in Phase 4, Phase 1 §G.1): writing material off as waste must never be
    // implied by the ability to edit a Job Order.
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionScrap)]
    [ProducesResponseType(typeof(SeparateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SeparateDto>> Scrap(Guid id, [FromBody] ScrapSeparateRequest request)
        => Ok(await _mediator.Send(new ScrapSeparateCommand(id, request.Reason)));
}

public record ReprocessSeparateRequest(DateTime OrderDate, string? Notes, Guid? StartingStageId = null);
public record ReprocessSeparatesBatchRequest(IReadOnlyList<Guid> SeparateIds, DateTime OrderDate, string? Notes = null, Guid? StartingStageId = null);
public record ScrapSeparateRequest(string Reason);
