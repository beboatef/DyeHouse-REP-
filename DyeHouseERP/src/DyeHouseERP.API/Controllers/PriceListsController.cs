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
/// The two commercial lists kept deliberately apart by spec section 34:
///   * /cost-rates      - ACTUAL COST LIST: stage + unit -> what the stage costs US
///   * /service-prices  - CUSTOMER SERVICE PRICE LIST: stage + customer + unit -> what the CUSTOMER is charged
///
/// Both are upserted against their unique indexes, and neither supports a delete:
/// a rate or price that already priced a Job Order must stay resolvable.
/// </summary>
[ApiController]
[Route("api/price-lists")]
[Authorize]
public class PriceListsController : ControllerBase
{
    private readonly ISender _mediator;
    public PriceListsController(ISender mediator) => _mediator = mediator;

    // ---------------- Actual Cost List (spec section 34A) ----------------

    [HttpGet("cost-rates")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingView)]
    [ProducesResponseType(typeof(List<StageCostRateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StageCostRateDto>>> GetCostRates(
        [FromQuery] Guid? stageDefinitionId, [FromQuery] bool? activeOnly)
        => Ok(await _mediator.Send(new GetStageCostRatesQuery(stageDefinitionId, activeOnly)));

    [HttpPost("cost-rates")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(StageCostRateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<StageCostRateDto>> SetCostRate([FromBody] SetStageCostRateCommand command)
        => Ok(await _mediator.Send(command));

    [HttpPost("cost-rates/{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(StageCostRateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<StageCostRateDto>> SetCostRateActive(Guid id, [FromBody] SetActiveRequest request)
        => Ok(await _mediator.Send(new SetStageCostRateActiveCommand(id, request.IsActive)));

    // ---------------- Customer Service Price List (spec section 36) ----------------

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
