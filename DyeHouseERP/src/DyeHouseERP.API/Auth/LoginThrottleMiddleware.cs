using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DyeHouseERP.API.Auth;

/// <summary>
/// Applies the login throttle to POST /api/auth/login ONLY - every other
/// endpoint passes straight through untouched (B3). When the (IP, username)
/// pair or the IP is locked out it answers 429 with Retry-After and a body
/// identical in shape to other error envelopes but with a generic title that
/// reveals nothing about whether the username exists or how many attempts
/// remain.
///
/// Placed before UseAuthentication in the pipeline: a locked-out request is
/// rejected without running any authentication logic at all.
///
/// Client IP comes from the connection (HttpContext.Connection.RemoteIpAddress)
/// via the throttle service's caller. Forwarded-headers trust is NOT added
/// here: this deployment's preview reaches the API through the Vite dev
/// proxy on loopback, and no trusted-proxy topology is configured, so
/// honoring X-Forwarded-From untrusted callers would let an attacker rotate
/// fake IPs to bypass the throttle entirely. If a real reverse proxy is
/// introduced later, UseForwardedHeaders with an explicit KnownProxies allow
/// list must be added first and this middleware will then see real client
/// IPs automatically.
/// </summary>
public class LoginThrottleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LoginRateLimiter _limiter;

    public LoginThrottleMiddleware(RequestDelegate next, LoginRateLimiter limiter)
    {
        _next = next;
        _limiter = limiter;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isLoginPost =
            HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);

        if (!isLoginPost)
        {
            await _next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var username = await ReadUsernameAsync(context) ?? string.Empty;
        var now = DateTime.UtcNow;

        if (_limiter.IsLockedOut(ip, username, now))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)TimeSpan
                .FromMinutes(GetOptions(context).LockoutMinutes)
                .TotalSeconds)
                .ToString();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"title\":\"Too many attempts. Try again later.\",\"status\":429}");
            return;
        }

        // Remember whether authentication succeeded so only failures count.
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                _limiter.RecordFailure(ip, username, DateTime.UtcNow);
            else if (context.Response.StatusCode == StatusCodes.Status200OK)
                _limiter.RecordSuccess(ip, username);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static LoginThrottleOptions GetOptions(HttpContext context)
        => context.RequestServices.GetService(typeof(LoginThrottleOptions)) as LoginThrottleOptions
           ?? LoginThrottleDefaults.Default;

    /// <summary>
    /// Reads the submitted username from the JSON body (once, buffered) so
    /// the pair dimension works. A malformed body still records the attempt
    /// under the empty username so garbage requests are not a free pass.
    /// </summary>
    private static async Task<string?> ReadUsernameAsync(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("username", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            context.Request.Body.Position = 0;
            return null;
        }
    }
}
