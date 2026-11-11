using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DyeHouseERP.API.Auth;

/// <summary>
/// Double-submit CSRF protection for the cookie session.
///
/// Cookies are attached by the browser automatically, so any other site could otherwise make the
/// browser POST to this API with the user's session. The defence: a random value is written to a
/// readable cookie at login, the SPA copies it into <c>X-CSRF-Token</c>, and the two must match.
/// A cross-site page cannot read the cookie (same-origin policy) and therefore cannot produce the
/// header, while the browser still sends the cookie.
///
/// Scope: only unsafe methods, only when the request is actually authenticated by the cookie. A
/// bearer-token request is not forgeable by a browser, so it is not affected - which keeps the
/// documented API/Swagger flow working unchanged. Safe methods are never blocked.
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    /// <summary>The header the SPA must echo the CSRF cookie value back into.</summary>
    public const string HeaderName = "X-CSRF-Token";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace
    };

    /// <summary>
    /// Sign-in itself is exempt: it is the endpoint that REPLACES the session, and requiring a token
    /// from the session being replaced would let a half-expired cookie state lock a user out of
    /// their own login screen. Cross-site login CSRF is not a meaningful escalation here.
    /// </summary>
    private static readonly PathString LoginPath = "/api/auth/login";

    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the CsrfProtectionMiddleware class.</summary>
    public CsrfProtectionMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Rejects cookie-authenticated unsafe requests whose CSRF header does not match the cookie.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (SafeMethods.Contains(context.Request.Method) ||
            context.Request.Path.Equals(LoginPath) ||
            !UsesCookieSession(context))
        {
            await _next(context);
            return;
        }

        var cookie = context.Request.Cookies[AuthCookies.Csrf];
        var header = context.Request.Headers[HeaderName].ToString();

        if (!TokensMatch(cookie, header))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                title = "This request is missing a valid CSRF token. Reload the page and sign in again.",
                status = 403,
                code = "CSRF_TOKEN_INVALID"
            }));
            return;
        }

        await _next(context);
    }

    /// <summary>True when this request carries the session cookie and no bearer header.</summary>
    private static bool UsesCookieSession(HttpContext context)
        => context.Request.Cookies.ContainsKey(AuthCookies.AccessToken) &&
           context.Request.Headers.Authorization.Count == 0;

    /// <summary>Comparison that does not exit on the first differing byte.</summary>
    private static bool TokensMatch(string? cookie, string? header)
    {
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header)) return false;

        var cookieBytes = Encoding.UTF8.GetBytes(cookie);
        var headerBytes = Encoding.UTF8.GetBytes(header);

        // FixedTimeEquals requires equal lengths; a length mismatch is a mismatch, and the token
        // length is fixed by the server, so this leaks nothing useful.
        if (cookieBytes.Length != headerBytes.Length) return false;

        return CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes);
    }
}
