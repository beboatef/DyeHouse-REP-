using DyeHouseERP.Application.CostAccounting.Commands;
using DyeHouseERP.Application.CostAccounting.DTOs;
using DyeHouseERP.Application.CostAccounting.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/production-orders/{productionOrderId:guid}/cost")]
[Authorize]
public class CostAccountingController : ControllerBase
{
    private readonly ISender _mediator;
    public CostAccountingController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<ProductionOrderCostDto>> Get(Guid productionOrderId)
        => Ok(await _mediator.Send(new GetProductionOrderCostQuery(productionOrderId)));

    [HttpPost("entries")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionEdit)]
    public async Task<ActionResult<CostEntryDto>> AddEntry(Guid productionOrderId, [FromBody] AddCostEntryRequest request)
        => Ok(await _mediator.Send(new CreateCostEntryCommand(productionOrderId, request.Category, request.Amount, request.EntryDate, request.Description)));
}

public record AddCostEntryRequest(Domain.Enums.CostCategory Category, decimal Amount, DateTime EntryDate, string? Description);
