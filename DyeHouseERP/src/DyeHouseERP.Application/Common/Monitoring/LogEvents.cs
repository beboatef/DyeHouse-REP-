namespace DyeHouseERP.Application.Common.Monitoring;

/// <summary>
/// Stable business-event names for the structured log. Alerts match on these
/// constants, so RENAMING A VALUE IS A BREAKING CHANGE for every dashboard and
/// alert rule built on top of the logs - add new names, never rename old ones.
/// Every emission puts the name in an <c>EventName</c> property at the log site;
/// see docs/operations/monitoring.md for meaning, severity and thresholds.
/// </summary>
public static class LogEvents
{
    /// <summary>A stock/document lock (sp_getapplock) timed out: 409 LOCK_TIMEOUT.</summary>
    public const string LockTimeout = "LOCK_TIMEOUT";

    /// <summary>A RowVersion conflict: someone committed the same row first (409 CONCURRENT_UPDATE).</summary>
    public const string ConcurrentUpdate = "CONCURRENT_UPDATE";

    /// <summary>A negative-stock movement posted under a consumed single-use approval.</summary>
    public const string NegativeStockOverrideCreated = "NegativeStockOverrideCreated";

    /// <summary>The login throttle rejected an attempt (429).</summary>
    public const string LoginThrottleTripped = "LoginThrottleTripped";

    /// <summary>An accounting period was closed.</summary>
    public const string PeriodClosed = "PeriodClosed";

    /// <summary>An accounting period was reopened.</summary>
    public const string PeriodReopened = "PeriodReopened";

    /// <summary>A single-use approval request was matched and marked Approved.</summary>
    public const string ApprovalConsumed = "ApprovalConsumed";

    /// <summary>Refresh tokens were revoked because the password changed or was reset.</summary>
    public const string RefreshTokenRevokedForPasswordChange = "RefreshTokenRevokedForPasswordChange";

    /// <summary>The API could not reach its database (bootstrap probe or health check).</summary>
    public const string DatabaseConnectionFailed = "DatabaseConnectionFailed";
}
