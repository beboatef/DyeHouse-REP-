using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Dashboard.DTOs;
using DyeHouseERP.Application.Dashboard.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ISender _mediator;
    public DashboardController(ISender mediator) => _mediator = mediator;

    [HttpGet("summary")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ProductionView)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary() => Ok(await _mediator.Send(new GetDashboardSummaryQuery()));
}
