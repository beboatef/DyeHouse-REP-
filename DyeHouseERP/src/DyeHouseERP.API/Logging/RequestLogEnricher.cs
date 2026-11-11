using Serilog.Core;
using Serilog.Events;

namespace DyeHouseERP.API.Logging;

/// <summary>
/// THE request-log enricher: exactly one place attaches per-request context to
/// every log event. It adds RequestId, the authenticated UserName (claims only -
/// never the body, so the login request, which is unauthenticated, contributes
/// NO name and no credential can leak through this path), the matched Route
/// template (low cardinality: "/api/customers/{id}", never a concrete id), and
/// the response StatusCode.
/// Elapsed ms is already rendered by UseSerilogRequestLogging's message
/// template; this enricher does not duplicate it.
/// What it NEVER logs: passwords, tokens, cookies, Authorization headers, or
/// request bodies. Serilog's request logging does not capture bodies, and this
/// enricher only reads the endpoint metadata, the status code and the already
/// authenticated ClaimsPrincipal - there is no code path here that touches a
/// secret, and the redaction unit test pins that down.
/// </summary>
public sealed class RequestLogEnricher : ILogEventEnricher
{
    /// <summary>The Serilog property name carrying the request id.</summary>
    public const string RequestIdProperty = "RequestId";
    /// <summary>The Serilog property name carrying the authenticated user name.</summary>
    public const string UserNameProperty = "UserName";
    /// <summary>The Serilog property name carrying the route template.</summary>
    public const string RouteProperty = "Route";
    /// <summary>The Serilog property name carrying the response status code.</summary>
    public const string StatusCodeProperty = "StatusCode";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance of the RequestLogEnricher class.</summary>
    public RequestLogEnricher(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    /// <summary>Adds the request id, user name, route template and status code (never secrets).</summary>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            RequestIdProperty, http.TraceIdentifier));

        // Authenticated claims only. On /api/auth/login (and every anonymous
        // call) there is no authenticated identity, so nothing is attached -
        // the submitted username stays in the request body where it belongs.
        var identity = http.User.Identity;
        if (identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(identity.Name))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                UserNameProperty, identity.Name));

        var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? http.Request.Path.Value;
        if (!string.IsNullOrEmpty(route))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                RouteProperty, route));

        // StatusCode is 200 while the response is still running; Serilog's own
        // request-completed event carries the final value. Both are useful: the
        // in-request value tells you the response had not started yet.
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            StatusCodeProperty, http.Response.StatusCode));
    }
}
