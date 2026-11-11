using System.Security.Claims;
using System.Security.Cryptography;
using DyeHouseERP.API.Auth;
using DyeHouseERP.Application.Auth.Commands;
using DyeHouseERP.Application.Auth.DTOs;
using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Application.Users.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Controllers;

/// <summary>Hosts the authentication (login, session refresh, logout, self-service password change) endpoints.</summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly IWebHostEnvironment _environment;

    /// <summary>Initializes a new instance of the AuthController class.</summary>
    public AuthController(ISender mediator, IWebHostEnvironment environment)
    {
        _mediator = mediator;
        _environment = environment;
    }

    /// <summary>Issues a JWT for a valid username/password. See UserSeeder for the default Development admin login.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.Send(command);

        // The browser never stores either token: both arrive as HttpOnly cookies, so a script -
        // including an injected one - cannot read them. The body still carries the access token for
        // non-browser clients that authenticate with an Authorization header.
        SetSessionCookies(result.Token, result.ExpiresAtUtc, result.RefreshToken, result.RefreshExpiresAtUtc);

        return Ok(result);
    }

    /// <summary>
    /// Rotates the session without asking for the password again: the refresh cookie presented here
    /// dies and a fresh pair takes its place. A failure answers 401 with no further detail.
    /// Presenting an already-rotated token is treated as theft and revokes every session of the
    /// user - unless it arrives inside the short reuse grace, where a concurrent tab race is the
    /// benign explanation; that case returns a new access token and leaves the refresh cookie alone.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RefreshSessionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RefreshSessionResultDto>> Refresh()
    {
        var presented = Request.Cookies[AuthCookies.RefreshToken];
        if (string.IsNullOrEmpty(presented))
            throw new InvalidCredentialsException();

        var result = await _mediator.Send(new RefreshSessionCommand(presented));

        SetSessionCookies(result.Token, result.ExpiresAtUtc, result.RefreshToken, result.RefreshExpiresAtUtc);

        return Ok(result);
    }

    /// <summary>
    /// The current-user read (Part 2): the one account query the must-change gate leaves open,
    /// so the frontend can learn who is signed in - including whether a password change is
    /// still forced - while every other endpoint answers 403.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me()
    {
        var users = await _mediator.Send(new GetUsersQuery());
        var me = users.FirstOrDefault(u =>
            string.Equals(u.Username, User.Identity?.Name, StringComparison.OrdinalIgnoreCase));
        return me is null ? NotFound() : Ok(me);
    }

    /// <summary>
    /// The signed-in user changes their OWN password (Part 2): the endpoint the must-change
    /// gate leaves open. Proves the current password, enforces the full policy, clears the
    /// flag, revokes every session and writes an audit entry. Needs no permission beyond a
    /// live session - a flagged user cannot do anything else until this succeeds.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeOwnPassword([FromBody] ChangeOwnPasswordRequest request)
    {
        await _mediator.Send(new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword));
        return NoContent();
    }

    /// <summary>
    /// Ends the session: revokes every refresh token of the signed-in user (when the request still
    /// carries an identity) and expires the cookies. A copied access token dies on its own short
    /// lifetime; the refresh half is killed here, immediately.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // The JWT maps "sub" to NameIdentifier, so this is the user id from a still-valid cookie.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var id))
            {
                await _mediator.Send(new RevokeUserRefreshTokensCommand(id, "Logout"));
            }
            else
            {
                // No identity means the access token has expired - the ordinary case after a tab
                // idled past its 30 minutes. The refresh cookie still identifies the session, so
                // it is revoked by token instead of being left alive for the rest of its week.
                var presented = Request.Cookies[AuthCookies.RefreshToken];
                if (!string.IsNullOrEmpty(presented))
                    await _mediator.Send(new RevokeSessionByRefreshTokenCommand(presented, "Logout"));
            }
        }
        catch { /* Logout must clear the browser even if revocation fails. */ }

        Response.Cookies.Delete(AuthCookies.AccessToken, SessionCookie(httpOnly: true, DateTimeOffset.UtcNow));
        Response.Cookies.Delete(AuthCookies.RefreshToken, SessionCookie(httpOnly: true, DateTimeOffset.UtcNow));
        Response.Cookies.Delete(AuthCookies.Csrf, SessionCookie(httpOnly: false, DateTimeOffset.UtcNow));
        return NoContent();
    }

    private void SetSessionCookies(string accessToken, DateTime accessExpiresAtUtc, string? refreshToken, DateTime refreshExpiresAtUtc)
    {
        Response.Cookies.Append(AuthCookies.AccessToken, accessToken, SessionCookie(httpOnly: true, accessExpiresAtUtc));

        // Empty means "the browser already holds the rotated cookie": a refresh that landed inside
        // the reuse grace (concurrent tabs) must not hand out a second refresh token.
        if (!string.IsNullOrEmpty(refreshToken))
            Response.Cookies.Append(AuthCookies.RefreshToken, refreshToken, SessionCookie(httpOnly: true, refreshExpiresAtUtc));

        // Readable half of the double-submit pair; see CsrfProtectionMiddleware. Kept stable across
        // refreshes when it already exists, so concurrent tabs never invalidate each other.
        var csrf = Request.Cookies[AuthCookies.Csrf];
        if (string.IsNullOrEmpty(csrf))
            csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var csrfExpiry = refreshExpiresAtUtc > DateTime.UtcNow ? refreshExpiresAtUtc : accessExpiresAtUtc;
        Response.Cookies.Append(AuthCookies.Csrf, csrf, SessionCookie(httpOnly: false, csrfExpiry));
    }

    private CookieOptions SessionCookie(bool httpOnly, DateTimeOffset expiresAtUtc) => new()
    {
        HttpOnly = httpOnly,
        // Secure is dropped only for the plain-http development origin (Vite proxy); every real
        // deployment is https and must never hand the cookie to a plaintext request.
        Secure = !_environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAtUtc,
        IsEssential = true
    };

/// <summary>Request payload to change own password.</summary>
/// <param name="CurrentPassword">The current password.</param>
/// <param name="NewPassword">The new password.</param>
public record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
}
