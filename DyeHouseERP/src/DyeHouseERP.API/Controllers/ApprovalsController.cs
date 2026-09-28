using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Approvals.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// Centralized Approval Center (spec section 44). Read-only by design: each row
/// links to the module whose own command performs the approval, so every
/// approval keeps its original permission, validation and audit trail instead
/// of being re-implemented here.
/// </summary>
[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalsController : ControllerBase
{
    private readonly ISender _mediator;
    public ApprovalsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ApprovalsView)]
    [ProducesResponseType(typeof(ApprovalCenterDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApprovalCenterDto>> Get([FromQuery] int recentDays = 30)
        => Ok(await _mediator.Send(new GetApprovalCenterQuery(recentDays)));
}
