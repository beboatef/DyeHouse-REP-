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
/// Client IP comes from the connection (HttpContext.Connection.RemoteIpAddress).
/// M8: Program.cs now enables UseForwardedHeaders with an EXPLICIT known-proxy
/// allow list, so when the API really is behind a reverse proxy this middleware
/// sees the real client IP instead of the proxy's. The allow list is what keeps
/// this safe: honoring X-Forwarded-For from untrusted senders would let an
/// attacker rotate fake IPs to bypass the throttle entirely. With no configured
/// proxy the headers stay ignored and the socket address is used.
/// </summary>
public class LoginThrottleMiddleware
{
    /// <summary>
    /// M8: the throttle key is normalized, so "Admin", "admin" and " ADMIN "
    /// all count against the SAME failure budget. Previously they were three
    /// separate buckets, which handed an attacker a free multiplier on the
    /// attempt limit.
    /// </summary>
    private const int MaxUsernameKeyLength = 100;

    /// <summary>
    /// M8: a login body is a username and a password - a few hundred bytes.
    /// Anything larger is rejected before it is buffered, so a single request
    /// cannot force the process to hold an arbitrary payload in memory.
    /// </summary>
    private const long MaxLoginBodyBytes = 4 * 1024;

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

        // M8: reject oversized payloads up front - before any buffering, before
        // any audit write, and before the throttle state is touched.
        if (context.Request.ContentLength is > MaxLoginBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"title\":\"Request body is too large.\",\"status\":413}");
            return;
        }

        var ip = NormalizeIp(context.Connection.RemoteIpAddress?.ToString());
        var username = NormalizeUsername(await ReadUsernameAsync(context));
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
    /// At most <see cref="MaxLoginBodyBytes"/> are ever buffered (M8).
    /// </summary>
    private static async Task<string?> ReadUsernameAsync(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering(bufferThreshold: 1024, bufferLimit: MaxLoginBodyBytes);
            using var reader = new StreamReader(
                context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024, leaveOpen: true);
            var buffer = new char[MaxLoginBodyBytes];
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length));
            context.Request.Body.Position = 0;

            using var doc = System.Text.Json.JsonDocument.Parse(new string(buffer, 0, read));
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

    /// <summary>Lower-cases the IP so the throttle key is stable across formatting differences.</summary>
    private static string NormalizeIp(string? ip)
        => string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim().ToLowerInvariant();

    /// <summary>
    /// Canonical form of the username used as the throttle key: trimmed,
    /// lower-cased and length-capped. The cap keeps an attacker from creating an
    /// unbounded number of distinct buckets by varying the string length - an
    /// over-long username still fails authentication, it just shares one bucket.
    /// </summary>
    private static string NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        var trimmed = username.Trim();
        return (trimmed.Length > MaxUsernameKeyLength ? trimmed[..MaxUsernameKeyLength] : trimmed)
            .ToLowerInvariant();
    }
}
