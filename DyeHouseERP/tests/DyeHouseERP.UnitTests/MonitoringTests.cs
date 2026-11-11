using System.Diagnostics.Metrics;
using DyeHouseERP.API.Logging;
using DyeHouseERP.Application.Common.Monitoring;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Part 3 (monitoring) tests: the single request enricher attaches the documented
/// properties and NEVER touches secrets; event names are pinned so alerts cannot
/// silently break on a rename; meters record without a listener.
/// </summary>
public class MonitoringTests
{
    private static LogEvent Capture(RequestLogEnricher enricher, string message = "x")
    {
        LogEvent? captured = null;
        new LoggerConfiguration()
            .Enrich.With(enricher)
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger()
            .Information(message);
        return captured!;
    }

    [Fact]
    public void Enricher_Attaches_RequestId_Route_And_Status_For_Anonymous_Requests()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/auth/login";
        http.Response.StatusCode = 200;
        var enricher = new RequestLogEnricher(new HttpContextAccessor { HttpContext = http });

        var enriched = Capture(enricher, "login attempt");

        enriched.Properties.Should().ContainKey(RequestLogEnricher.RequestIdProperty);
        enriched.Properties.Should().ContainKey(RequestLogEnricher.RouteProperty);
        enriched.Properties.Should().ContainKey(RequestLogEnricher.StatusCodeProperty);
        // Anonymous: no authenticated identity, so NO user name - the submitted
        // username stays in the request body, never in the log.
        enriched.Properties.Should().NotContainKey(RequestLogEnricher.UserNameProperty);
    }

    [Fact]
    public void Enricher_Attaches_Authenticated_UserName_And_Route_Template()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/customers/{id}"),
            0, new EndpointMetadataCollection(), "test"));
        http.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
#pragma warning disable CA1303 // test fixture literal, not user-visible text
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "sara") }, "test"));
#pragma warning restore CA1303
        var enricher = new RequestLogEnricher(new HttpContextAccessor { HttpContext = http });

        var enriched = Capture(enricher);

        Scalar(enriched, RequestLogEnricher.UserNameProperty).Should().Be("sara");
        Scalar(enriched, RequestLogEnricher.RouteProperty).Should().Be("/api/customers/{id}");
    }

    [Fact]
    public void EventNames_Are_Stable_Constants()
    {
        // If any of these values changes, every Seq/Elasticsearch alert matching
        // on EventName breaks silently. The test pins the wire values.
        LogEvents.LockTimeout.Should().Be("LOCK_TIMEOUT");
        LogEvents.ConcurrentUpdate.Should().Be("CONCURRENT_UPDATE");
        LogEvents.NegativeStockOverrideCreated.Should().Be("NegativeStockOverrideCreated");
        LogEvents.LoginThrottleTripped.Should().Be("LoginThrottleTripped");
        LogEvents.PeriodClosed.Should().Be("PeriodClosed");
        LogEvents.PeriodReopened.Should().Be("PeriodReopened");
        LogEvents.ApprovalConsumed.Should().Be("ApprovalConsumed");
        LogEvents.RefreshTokenRevokedForPasswordChange.Should().Be("RefreshTokenRevokedForPasswordChange");
        LogEvents.DatabaseConnectionFailed.Should().Be("DatabaseConnectionFailed");
    }

    [Fact]
    public void Meters_Record_Without_A_Listener_And_With_One()
    {
        // No listener: must not throw (the production default when OTLP is off).
        DyeHouseMetrics.RequestDurationMs.Record(12.5, new KeyValuePair<string, object?>("route", "t"));
        DyeHouseMetrics.ServerErrors.Add(1, new KeyValuePair<string, object?>("route", "t"));
        DyeHouseMetrics.RecordLockAcquire(3.0, "Exclusive", acquired: true);
        DyeHouseMetrics.RecordLockAcquire(5000.0, "Exclusive", acquired: false);

        // With a listener: the instruments actually emit.
        var durations = new List<double>();
        var timeouts = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DyeHouseMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((i, m, _, _) =>
        {
            if (i.Name == "dyehouse.request.duration") durations.Add(m);
        });
        listener.SetMeasurementEventCallback<long>((i, m, _, _) =>
        {
            if (i.Name == "dyehouse.stock.lock_timeout") timeouts.Add(m);
        });
        listener.Start();

        DyeHouseMetrics.RequestDurationMs.Record(7.5, new KeyValuePair<string, object?>("route", "t"));
        DyeHouseMetrics.RecordLockAcquire(1.0, "Shared", acquired: false);

        durations.Should().ContainSingle().Which.Should().Be(7.5);
        timeouts.Should().ContainSingle().Which.Should().Be(1);
    }

    private static object? Scalar(LogEvent e, string key)
        => (e.Properties[key] as ScalarValue)?.Value;

    private sealed class DelegatingSink : ILogEventSink
    {
        private readonly Action<LogEvent> _write;
        public DelegatingSink(Action<LogEvent> write) => _write = write;
        public void Emit(LogEvent logEvent) => _write(logEvent);
    }
}
