using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Application.Users.Queries;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>
/// User/login management. Access is decided by the GRANULAR permissions on each action
/// (users.manage for the account itself, roles.manage for changing a user's permissions) -
/// an administrator simply holds those rights like any other user, and a non-admin user the
/// administrator grants them to can use them. Authentication is always required.
///
/// The last-admin and self-lockout safeguards (UpdateUserCommand - an admin cannot change
/// their own permission set; DeactivateUserCommand - the last active admin cannot be
/// deactivated) remain enforced server-side in the command handlers, not by this attribute.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
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

    /// <summary>
    /// Edits a login's display name and permission set (spec section 41).
    /// Guarded by roles.manage rather than users.manage because it changes which
    /// permissions the account holds - that constant existed but was unused until now.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RolesManage)]
    public async Task<ActionResult<UserDto>> Update(Guid id, [FromBody] UpdateUserBody body)
        => Ok(await _mediator.Send(new UpdateUserCommand(id, body.DisplayName, body.Roles)));

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

public record UpdateUserBody(string DisplayName, List<string> Roles);
