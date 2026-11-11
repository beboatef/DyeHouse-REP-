using System.Security.Claims;
using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.API.Middleware;

/// <summary>
/// Part 2 (password policy): while the signed-in account is flagged MustChangePassword, every
/// endpoint EXCEPT the password-change endpoint, logout and the current-user read answers
/// 403 with code PASSWORD_CHANGE_REQUIRED. The flag is read live from the database on every
/// request (never from a JWT claim), so an administrator reset takes effect on the very next
/// call even though the user's access token is still valid.
/// Open by design: anonymous requests (login, /health), POST /api/auth/change-password
/// (the way out), POST /api/auth/logout, GET /api/auth/me, and the readiness probe's own
/// dependency - none of these need the flag cleared. Everything else waits.
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the MustChangePasswordMiddleware class.</summary>
    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Answers 403 + PASSWORD_CHANGE_REQUIRED to every business call while the flag stands, except the change-password, logout, self-read, login, refresh and health paths.</summary>
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var identity = context.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id))
        {
            await _next(context);
            return;
        }

        var mustChange = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.MustChangePassword)
            .FirstOrDefaultAsync();

        if (!mustChange)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            "{\"title\":\"You must change your password before using the system.\"," +
            "\"status\":403,\"code\":\"PASSWORD_CHANGE_REQUIRED\"}");
    }

    internal static bool IsExempt(PathString path)
    {
        if (path.StartsWithSegments("/api/auth/change-password", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWithSegments("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWithSegments("/api/auth/me", StringComparison.OrdinalIgnoreCase))
            return true;
        // Login/refresh must stay reachable (they mint the session that performs the change);
        // health probes carry no identity and are unaffected, but exempting them keeps the
        // gate's intent explicit if a prober ever presents a token.
        if (path.StartsWithSegments("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWithSegments("/api/auth/refresh", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
