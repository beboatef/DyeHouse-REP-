using DyeHouseERP.API.Authorization;
using DyeHouseERP.Application.Settings.Commands;
using DyeHouseERP.Application.Settings.DTOs;
using DyeHouseERP.Application.Settings.Queries;
using DyeHouseERP.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Company branding (name + logo) - spec section 47 Administration -> Settings.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISender _mediator;
    /// <summary>Initializes a new instance of the SettingsController class.</summary>
    public SettingsController(ISender mediator) => _mediator = mediator;

    /// <summary>Anonymous on purpose - the login page and sidebar both need the logo/name before the user has a token.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<CompanySettingsDto>> Get() => Ok(await _mediator.Send(new GetCompanySettingsQuery()));

    /// <summary>Updates company settings.</summary>
    /// <remarks>Route: PUT api/settings. Requires the 'settings.manage' permission.</remarks>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<CompanySettingsDto>> Update([FromBody] UpdateCompanySettingsCommand command)
        => Ok(await _mediator.Send(command));

    /// <summary>Part 2: tunes password max age and session idle timeout (settings.manage).</summary>
    [HttpPut("password-policy")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Policy = PermissionPolicy.Prefix + Permissions.SettingsManage)]
    public async Task<ActionResult<CompanySettingsDto>> UpdatePasswordPolicy([FromBody] UpdatePasswordPolicyCommand command)
        => Ok(await _mediator.Send(command));
}
