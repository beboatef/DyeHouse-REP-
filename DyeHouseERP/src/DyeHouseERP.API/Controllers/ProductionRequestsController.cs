using DyeHouseERP.Application.CustomerPortal.Commands;
using DyeHouseERP.Application.CustomerPortal.DTOs;
using DyeHouseERP.Application.CustomerPortal.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Customer portal entry point (spec section 36): a customer submits a
/// request here; staff approve/reject or convert it into a real Production
/// Order. The customer never gets direct access to internal administration.
/// </summary>
[ApiController]
[Route("api/production-requests")]
[Authorize]
public class ProductionRequestsController : ControllerBase
{
    private readonly ISender _mediator;
    public ProductionRequestsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<List<ProductionRequestDto>>> Get([FromQuery] Guid? customerId, [FromQuery] ProductionRequestStatus? status)
        => Ok(await _mediator.Send(new GetProductionRequestsQuery(customerId, status)));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionCreate)]
    public async Task<ActionResult<ProductionRequestDto>> Create([FromBody] CreateProductionRequestCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    public async Task<ActionResult<ProductionRequestDto>> Approve(Guid id, [FromBody] ApproveProductionRequestRequest request)
        => Ok(await _mediator.Send(new ApproveProductionRequestCommand(id, request.StaffNotes)));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    public async Task<ActionResult<ProductionRequestDto>> Reject(Guid id, [FromBody] RejectProductionRequestRequest request)
        => Ok(await _mediator.Send(new RejectProductionRequestCommand(id, request.Reason)));

    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionCreate)]
    public async Task<ActionResult<ProductionRequestDto>> Convert(Guid id, [FromBody] ConvertProductionRequestRequest request)
        => Ok(await _mediator.Send(new ConvertProductionRequestCommand(id, request.OrderDate)));
}

public record ApproveProductionRequestRequest(string? StaffNotes);
public record RejectProductionRequestRequest(string Reason);
public record ConvertProductionRequestRequest(DateTime OrderDate);
