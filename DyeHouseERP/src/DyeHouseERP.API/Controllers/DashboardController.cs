using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Dashboard.DTOs;
using DyeHouseERP.Application.Dashboard.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Hosts the dashboard figures endpoints.</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the DashboardController class.</summary>
    public DashboardController(ISender mediator) => _mediator = mediator;

    /// <summary>Gets summary.</summary>
    /// <remarks>Route: GET api/dashboard/summary. Requires the 'production.view' permission.</remarks>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary() => Ok(await _mediator.Send(new GetDashboardSummaryQuery()));
}
