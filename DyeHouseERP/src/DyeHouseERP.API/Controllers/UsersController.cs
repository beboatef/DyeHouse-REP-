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
    /// <summary>Initializes a new instance of the UsersController class.</summary>
    public UsersController(ISender mediator) => _mediator = mediator;

    /// <summary>Lists users.</summary>
    /// <remarks>Route: GET api/users. Requires the 'users.manage' permission.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<ActionResult<List<UserDto>>> Get() => Ok(await _mediator.Send(new GetUsersQuery()));

    /// <summary>Creates users.</summary>
    /// <remarks>Route: POST api/users. Requires the 'users.manage' permission.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.RolesManage)]
    public async Task<ActionResult<UserDto>> Update(Guid id, [FromBody] UpdateUserBody body)
        => Ok(await _mediator.Send(new UpdateUserCommand(id, body.DisplayName, body.Roles)));

    /// <summary>Runs the deactivate operation.</summary>
    /// <remarks>Route: POST api/users/{id:guid}/deactivate. Requires the 'users.manage' permission.</remarks>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.UsersManage)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await _mediator.Send(new DeactivateUserCommand(id));
        return NoContent();
    }

    // Part 2: resets are governed by the dedicated users.reset_password permission, so the
    // right can be delegated (e.g. to a helpdesk role) without granting full users.manage.
    // Administrators keep working through the "admin" bypass, exactly as before.
    /// <summary>Changes password.</summary>
    /// <remarks>Route: POST api/users/{id:guid}/change-password. Requires the 'users.reset_password' permission.</remarks>
    [HttpPost("{id:guid}/change-password")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.ResetPassword)]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest request)
    {
        await _mediator.Send(new ChangePasswordCommand(id, request.NewPassword));
        return NoContent();
    }
}

/// <summary>Request payload to change password.</summary>
/// <param name="NewPassword">The new password.</param>
public record ChangePasswordRequest(string NewPassword);

/// <summary>Request payload to update user.</summary>
/// <param name="DisplayName">The display name.</param>
/// <param name="Roles">The roles.</param>
public record UpdateUserBody(string DisplayName, List<string> Roles);
