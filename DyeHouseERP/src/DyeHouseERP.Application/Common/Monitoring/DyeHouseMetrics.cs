using System.Diagnostics.Metrics;

namespace DyeHouseERP.Application.Common.Monitoring;

/// <summary>
/// Process-wide OpenTelemetry instruments for DyeHouseERP. The <see cref="Meter"/>
/// and its instruments ALWAYS exist - recording a measurement without a listener
/// is a no-op, so there is no "metrics off" code path and no per-request cost
/// beyond a few nanoseconds when nothing listens.
/// Export is configured in the API composition root (Program.cs): when the
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable is set the OTLP
/// exporter ships these instruments to whatever backend the operator runs
/// (Seq, Prometheus, Elasticsearch - the code has no opinion). When it is unset
/// nothing is exported and the app behaves exactly as before. Do NOT add a
/// hard dependency on a specific backend here.
/// </summary>
public static class DyeHouseMetrics
{
    /// <summary>Meter name - the one name a listener subscribes to.</summary>
    public const string MeterName = "DyeHouseERP";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>HTTP request duration in milliseconds, tagged with route. Unit tests read it with a MeterListener.</summary>
    public static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "dyehouse.request.duration",
        unit: "ms",
        description: "HTTP request duration by route.");

    /// <summary>Responses with status 500, tagged with route.</summary>
    public static readonly Counter<long> ServerErrors = Meter.CreateCounter<long>(
        "dyehouse.request.5xx",
        unit: "{response}",
        description: "HTTP 500 responses by route.");

    /// <summary>Time spent waiting on sp_getapplock, in milliseconds, tagged with lock mode.</summary>
    public static readonly Histogram<double> LockWaitMs = Meter.CreateHistogram<double>(
        "dyehouse.stock.lock_wait",
        unit: "ms",
        description: "Time spent acquiring a stock/document lock.");

    /// <summary>sp_getapplock acquisitions that timed out.</summary>
    public static readonly Counter<long> LockTimeouts = Meter.CreateCounter<long>(
        "dyehouse.stock.lock_timeout",
        unit: "{timeout}",
        description: "Stock/document lock acquisitions that timed out.");

    /// <summary>Records one stock-lock acquisition attempt (always the wait; the timeout only on failure).</summary>
    public static void RecordLockAcquire(double elapsedMs, string lockMode, bool acquired)
    {
        LockWaitMs.Record(elapsedMs, new KeyValuePair<string, object?>("lock.mode", lockMode));
        if (!acquired)
            LockTimeouts.Add(1, new KeyValuePair<string, object?>("lock.mode", lockMode));
    }
}
