using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Application.Users.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>User/login management - restricted to the "admin" role only.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = "admin")]
public class UsersController : ControllerBase
{
    private readonly ISender _mediator;
    public UsersController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<ActionResult<List<UserDto>>> Get() => Ok(await _mediator.Send(new GetUsersQuery()));

    [HttpPost]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { }, result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await _mediator.Send(new DeactivateUserCommand(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/change-password")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest request)
    {
        await _mediator.Send(new ChangePasswordCommand(id, request.NewPassword));
        return NoContent();
    }
}

public record ChangePasswordRequest(string NewPassword);
