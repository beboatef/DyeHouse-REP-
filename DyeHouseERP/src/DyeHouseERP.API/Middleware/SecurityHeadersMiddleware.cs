namespace DyeHouseERP.API.Middleware;

/// <summary>
/// Response hardening headers, applied to every response the API produces.
///
/// The API only returns JSON and files, so the important part is what a browser must NOT do with
/// it: sniff a response into another content type, frame it, leak the URL in a Referer, or hand a
/// script access to the document. The SPA is served by its own host, so its own CSP belongs there;
/// here the policy is scoped to /api because Swagger UI (development only) loads its own scripts
/// and assets and would stop working under <c>default-src 'none'</c>.
///
/// Registered before the exception middleware on purpose: an error response is still a response
/// and must carry the same headers as a successful one.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the SecurityHeadersMiddleware class.</summary>
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Adds the standard security response headers.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Never let a browser re-interpret a JSON (or uploaded) response as HTML/JS.
        headers["X-Content-Type-Options"] = "nosniff";
        // The API's URLs are internal tokens/ids; do not leak them to third-party hosts.
        headers["Referrer-Policy"] = "no-referrer";
        // Belt and braces with the CSP frame-ancestors below, for clients that ignore CSP.
        headers["X-Frame-Options"] = "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        }

        await _next(context);
    }
}
