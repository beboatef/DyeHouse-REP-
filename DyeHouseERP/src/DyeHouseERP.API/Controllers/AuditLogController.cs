using DyeHouseERP.Application.Audit.DTOs;
using DyeHouseERP.Application.Audit.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Audit trail (spec sections 35, 42) - who did what, when, before/after. Admin-only.</summary>
[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = "admin")]
public class AuditLogController : ControllerBase
{
    private readonly ISender _mediator;
    public AuditLogController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.AuditView)]
    public async Task<ActionResult<List<AuditLogEntryDto>>> Get(
        [FromQuery] string? entityName, [FromQuery] string? userName, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _mediator.Send(new GetAuditLogQuery(entityName, userName, from, to)));
}
