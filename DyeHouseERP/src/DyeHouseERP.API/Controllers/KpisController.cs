using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Kpis.DTOs;
using DyeHouseERP.Application.Kpis.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Management KPIs computed read-only from the append-only ledgers and
/// existing documents (spec Part 1). Nothing here writes anything; every
/// result carries the date range and the timezone used, and division by zero
/// returns null. Formulas are documented in docs/kpis/definitions.md.
/// </summary>
[ApiController]
[Route("api/kpis")]
[Authorize]
public class KpisController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the KpisController class.</summary>
    public KpisController(ISender mediator) => _mediator = mediator;

    /// <summary>Gets waste rate kpi.</summary>
    /// <remarks>Route: GET api/kpis/waste-rate. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("waste-rate")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(WasteRateKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WasteRateKpiResult>> GetWasteRate(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? customerId, [FromQuery] Guid? productionOrderId)
        => Ok(await _mediator.Send(new GetWasteRateKpiQuery(from, to, customerId, productionOrderId)));

    /// <summary>Gets lead time kpi.</summary>
    /// <remarks>Route: GET api/kpis/lead-time. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("lead-time")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(LeadTimeKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LeadTimeKpiResult>> GetLeadTime(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetLeadTimeKpiQuery(from, to, customerId)));

    /// <summary>Gets machine utilisation kpi.</summary>
    /// <remarks>Route: GET api/kpis/machine-utilisation. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("machine-utilisation")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(MachineUtilisationKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MachineUtilisationKpiResult>> GetMachineUtilisation(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetMachineUtilisationKpiQuery(from, to)));

    /// <summary>Gets throughput kpi.</summary>
    /// <remarks>Route: GET api/kpis/throughput. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("throughput")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(ThroughputKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThroughputKpiResult>> GetThroughput(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetThroughputKpiQuery(from, to)));

    /// <summary>Gets customer exposure kpi.</summary>
    /// <remarks>Route: GET api/kpis/customer-exposure. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("customer-exposure")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(CustomerExposureKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerExposureKpiResult>> GetCustomerExposure([FromQuery] Guid? customerId)
        => Ok(await _mediator.Send(new GetCustomerExposureKpiQuery(customerId)));

    /// <summary>Gets cheques due kpi.</summary>
    /// <remarks>Route: GET api/kpis/cheques-due. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("cheques-due")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(ChequesDueKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChequesDueKpiResult>> GetChequesDue([FromQuery] int days = 7)
        => Ok(await _mediator.Send(new GetChequesDueKpiQuery(days)));

    /// <summary>Gets governance kpi.</summary>
    /// <remarks>Route: GET api/kpis/governance. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("governance")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(GovernanceKpiResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GovernanceKpiResult>> GetGovernance(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetGovernanceKpiQuery(from, to)));

    /// <summary>Gets kpi summary.</summary>
    /// <remarks>Route: GET api/kpis/summary. Requires the 'kpis.view' permission.</remarks>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.KpisView)]
    [ProducesResponseType(typeof(KpiSummaryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KpiSummaryResult>> GetSummary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetKpiSummaryQuery(from, to)));
}
