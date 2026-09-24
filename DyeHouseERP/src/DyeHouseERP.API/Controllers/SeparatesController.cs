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

    [HttpPost("{id:guid}/reprocess")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionReprocess)]
    [ProducesResponseType(typeof(ProductionOrderDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductionOrderDto>> Reprocess(Guid id, [FromBody] ReprocessSeparateRequest request)
        => Ok(await _mediator.Send(new ReprocessSeparateCommand(id, request.OrderDate, request.Notes)));

    [HttpPost("{id:guid}/scrap")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    [ProducesResponseType(typeof(SeparateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SeparateDto>> Scrap(Guid id, [FromBody] ScrapSeparateRequest request)
        => Ok(await _mediator.Send(new ScrapSeparateCommand(id, request.Reason)));
}

public record ReprocessSeparateRequest(DateTime OrderDate, string? Notes);
public record ScrapSeparateRequest(string Reason);
