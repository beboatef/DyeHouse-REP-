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
    /// <summary>Initializes a new instance of the PriceListsController class.</summary>
    public PriceListsController(ISender mediator) => _mediator = mediator;

    // ---------------- Customer Service Price List (spec sections 34 + 36) ----------------

    /// <summary>Gets service prices.</summary>
    /// <remarks>Route: GET api/price-lists/service-prices. Requires the 'pricing.view' permission.</remarks>
    [HttpGet("service-prices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingView)]
    [ProducesResponseType(typeof(List<CustomerServicePriceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<CustomerServicePriceDto>>> GetServicePrices(
        [FromQuery] Guid? customerId, [FromQuery] bool? activeOnly,
        [FromQuery] Guid? stageDefinitionId, [FromQuery] bool? generalOnly)
        => Ok(await _mediator.Send(new GetCustomerServicePricesQuery(customerId, activeOnly, stageDefinitionId, generalOnly)));

    /// <summary>Sets service price.</summary>
    /// <remarks>Route: POST api/price-lists/service-prices. Requires the 'pricing.manage' permission.</remarks>
    [HttpPost("service-prices")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(CustomerServicePriceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CustomerServicePriceDto>> SetServicePrice([FromBody] SetCustomerServicePriceCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Sets service price active.</summary>
    /// <remarks>Route: POST api/price-lists/service-prices/{id:guid}/active. Requires the 'pricing.manage' permission.</remarks>
    [HttpPost("service-prices/{id:guid}/active")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.PricingManage)]
    [ProducesResponseType(typeof(CustomerServicePriceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CustomerServicePriceDto>> SetServicePriceActive(Guid id, [FromBody] SetActiveRequest request)
        => Ok(await _mediator.Send(new SetCustomerServicePriceActiveCommand(id, request.IsActive)));
}

/// <summary>Request payload to set active.</summary>
/// <param name="IsActive">Whether is active.</param>
public record SetActiveRequest(bool IsActive);
