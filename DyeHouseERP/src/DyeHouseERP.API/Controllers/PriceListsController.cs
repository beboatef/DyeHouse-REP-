using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.PriceLists.Commands;
using DyeHouseERP.Application.PriceLists.DTOs;
using DyeHouseERP.Application.PriceLists.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// The customer service price list (spec sections 34 + 36): stage + customer + unit
/// -> what the CUSTOMER is charged, plus the per-Job-Order price snapshot.
///
/// S8: the fixed per-stage "actual cost list" (StageCostRate) was removed. Factory
/// actual cost comes from posted transactions and is deliberately NOT a price list,
/// so customer revenue and factory cost can never be conflated.
///
/// Rows are upserted against their unique indexes and never deleted: a price that
/// already priced a Job Order must stay resolvable.
/// </summary>
[ApiController]
[Route("api/price-lists")]
[Authorize]
public class PriceListsController : ControllerBase
{
    private readonly ISender _mediator;
    public PriceListsController(ISender mediator) => _mediator = mediator;

    // ---------------- Customer Service Price List (spec sections 34 + 36) ----------------

    [HttpGet("service-prices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingView)]
    [ProducesResponseType(typeof(List<CustomerServicePriceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerServicePriceDto>>> GetServicePrices(
        [FromQuery] Guid? customerId, [FromQuery] bool? activeOnly,
        [FromQuery] Guid? stageDefinitionId, [FromQuery] bool? generalOnly)
        => Ok(await _mediator.Send(new GetCustomerServicePricesQuery(customerId, activeOnly, stageDefinitionId, generalOnly)));

    [HttpPost("service-prices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(CustomerServicePriceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerServicePriceDto>> SetServicePrice([FromBody] SetCustomerServicePriceCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("service-prices/{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(CustomerServicePriceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerServicePriceDto>> SetServicePriceActive(Guid id, [FromBody] SetActiveRequest request)
        => Ok(await _mediator.Send(new SetCustomerServicePriceActiveCommand(id, request.IsActive)));
}

public record SetActiveRequest(bool IsActive);
