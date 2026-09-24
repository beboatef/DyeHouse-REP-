using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.PeriodClosing.Commands;
using DyeHouseERP.Application.PeriodClosing.DTOs;
using DyeHouseERP.Application.PeriodClosing.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Period closing (spec section 43) - admin only, since reopening a closed period is a significant, audited action.</summary>
[ApiController]
[Route("api/period-closes")]
[Authorize(Roles = "admin")]
public class PeriodClosingController : ControllerBase
{
    private readonly ISender _mediator;
    public PeriodClosingController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<List<PeriodCloseDto>>> Get() => Ok(await _mediator.Send(new GetPeriodClosesQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<PeriodCloseDto>> Close([FromBody] ClosePeriodCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<PeriodCloseDto>> Reopen(Guid id, [FromBody] ReopenPeriodRequest request)
        => Ok(await _mediator.Send(new ReopenPeriodCommand(id, request.Reason)));
}

public record ReopenPeriodRequest(string Reason);
