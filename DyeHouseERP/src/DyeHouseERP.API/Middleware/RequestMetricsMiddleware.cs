using System.Diagnostics;
using DyeHouseERP.Application.Common.Monitoring;

namespace DyeHouseERP.API.Middleware;

/// <summary>
/// Records OpenTelemetry request metrics: duration by route and 5xx by route.
/// The route tag is the matched route TEMPLATE ("/api/customers/{id}"), falling
/// back to the raw path only for unmatched requests, so tag cardinality stays
/// bounded. Recording without a listener is a no-op; export is configured in
/// Program.cs and only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
/// </summary>
public class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the RequestMetricsMiddleware class.</summary>
    public RequestMetricsMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Times the request and records the route duration and 5xx counters.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        await _next(context);
        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? context.Request.Path.Value
            ?? "unknown";

        DyeHouseMetrics.RequestDurationMs.Record(
            elapsedMs, new KeyValuePair<string, object?>("route", route));

        if (context.Response.StatusCode >= 500)
            DyeHouseMetrics.ServerErrors.Add(
                1, new KeyValuePair<string, object?>("route", route));
    }
}
