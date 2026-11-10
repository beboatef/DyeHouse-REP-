using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DyeHouseERP.API.Auth;

/// <summary>
/// Configuration for the login throttle. Bound from the "LoginThrottle"
/// configuration section; every value has a safe default so the protection
/// works with no configuration at all (B3).
/// </summary>
public class LoginThrottleOptions
{
    public const string SectionName = "LoginThrottle";

    /// <summary>Failed attempts allowed per (IP, username) window before lockout. Default 5.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long a failure stays counted. Default 15 minutes.</summary>
    public int FailureWindowMinutes { get; set; } = 15;

    /// <summary>How long a locked-out (IP, username) pair stays locked. Default 15 minutes.</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Failed attempts from one IP across ALL usernames before the IP itself is throttled (stops one host spraying many usernames). Default 20. Set 0 to disable.</summary>
    public int MaxFailedAttemptsPerIp { get; set; } = 20;
}

/// <summary>
/// Tracks FAILED login attempts per (IP, username) pair and per IP, and says
/// whether a new attempt should be rejected (B3). Thread-safe: all state
/// lives in ConcurrentDictionary with atomic updates, so parallel requests
/// cannot lose-count (the lockout decision is a single atomic snapshot read).
///
/// Design points:
/// - Only FAILED attempts count; successful logins never add pressure, and a
///   successful login clears that username's failure record, so a legitimate
///   user who mistypes once is never punished for other traffic (no DoS).
/// - Two independent dimensions: the (IP, username) pair blocks a targeted
///   guess from anywhere, and the per-IP total blocks one host spraying many
///   usernames. A locked IP+username pair rejects immediately without
///   touching authentication, so the response cannot leak whether the
///   username exists.
/// - Entries are lazily evicted on read/write by timestamp, so memory stays
///   bounded without a background timer.
/// - This is per-instance in-memory state: it protects a single API process.
///   Behind multiple load-balanced instances each enforces its own budget
///   (the effective limit multiplies by instance count). No distributed
///   store is added - this deployment runs a single instance.
/// </summary>
public class LoginRateLimiter
{
    /// <summary>
    /// Hard ceiling on tracked keys. Without it an unauthenticated caller creates one entry per
    /// (IP, username) pair for free, and the throttle itself becomes the memory-exhaustion vector
    /// it exists to prevent. A key beyond the ceiling is simply not tracked (its login still fails
    /// authentication); keys already tracked keep counting.
    /// </summary>
    private const int MaxTrackedPairs = 20_000;
    private const int MaxTrackedIps = 10_000;

    /// <summary>Cleanup cadence - once a minute instead of once per failed request.</summary>
    private static readonly long EvictionIntervalTicks = TimeSpan.FromMinutes(1).Ticks;

    private readonly LoginThrottleOptions _options;
    private readonly ConcurrentDictionary<(string Ip, string Username), FailureRecord> _perPair = new();
    private readonly ConcurrentDictionary<string, IpFailureRecord> _perIp = new();
    private long _lastEvictionTicksUtc;

    private sealed class FailureRecord
    {
        public int Count;
        public DateTime WindowStartUtc;
        public DateTime LockedUntilUtc;
    }

    private sealed class IpFailureRecord
    {
        public int Count;
        public DateTime WindowStartUtc;
    }

    public LoginRateLimiter(LoginThrottleOptions options) => _options = options;

    /// <summary>True when this (IP, username) pair - or this IP overall - is currently locked out.</summary>
    public bool IsLockedOut(string ip, string username, DateTime nowUtc)
    {
        if (_options.MaxFailedAttempts > 0 &&
            _perPair.TryGetValue((ip, username), out var pair) &&
            pair.LockedUntilUtc > nowUtc)
            return true;

        if (_options.MaxFailedAttemptsPerIp > 0 &&
            _perIp.TryGetValue(ip, out var ipRecord) &&
            IsLive(ipRecord.WindowStartUtc, nowUtc) &&
            ipRecord.Count >= _options.MaxFailedAttemptsPerIp)
            return true;

        return false;
    }

    /// <summary>Records one failed authentication for both dimensions.</summary>
    public void RecordFailure(string ip, string username, DateTime nowUtc)
    {
        MaybeEvictExpired(nowUtc);

        if (_options.MaxFailedAttempts > 0)
        {
            var key = (ip, username);
            if (_perPair.ContainsKey(key) || _perPair.Count < MaxTrackedPairs)
            {
                _perPair.AddOrUpdate(
                    key,
                    _ => new FailureRecord { Count = 1, WindowStartUtc = nowUtc, LockedUntilUtc = DateTime.MinValue },
                    (_, record) =>
                    {
                        if (!IsLive(record.WindowStartUtc, nowUtc))
                        {
                            record.Count = 1;
                            record.WindowStartUtc = nowUtc;
                        }
                        else
                        {
                            record.Count++;
                        }

                        if (record.Count >= _options.MaxFailedAttempts)
                            record.LockedUntilUtc = nowUtc.AddMinutes(_options.LockoutMinutes);

                        return record;
                    });
            }
        }

        if (_options.MaxFailedAttemptsPerIp > 0 &&
            (_perIp.ContainsKey(ip) || _perIp.Count < MaxTrackedIps))
        {
            _perIp.AddOrUpdate(
                ip,
                _ => new IpFailureRecord { Count = 1, WindowStartUtc = nowUtc },
                (_, record) =>
                {
                    if (!IsLive(record.WindowStartUtc, nowUtc))
                    {
                        record.Count = 1;
                        record.WindowStartUtc = nowUtc;
                    }
                    else
                    {
                        record.Count++;
                    }
                    return record;
                });
        }
    }

    /// <summary>
    /// Clears the per-pair failure count after a SUCCESSFUL authentication,
    /// so old mistakes never keep a legitimate user locked out.
    /// (The per-IP total is deliberately left alone: a burst of failures from
    /// one host followed by one success is still a spraying pattern.)
    /// </summary>
    public void RecordSuccess(string ip, string username)
        => _perPair.TryRemove((ip, username), out _);

    /// <summary>Exposed for tests.</summary>
    internal int PairFailureCountForTest(string ip, string username)
        => _perPair.TryGetValue((ip, username), out var r) && IsLive(r.WindowStartUtc, DateTime.UtcNow) ? r.Count : 0;

    /// <summary>
    /// Runs the eviction at most once a minute, and only on one thread. Doing it inline on every
    /// failed login made each request O(tracked keys) - a CPU amplifier on the very endpoint that
    /// is supposed to absorb the attack.
    /// </summary>
    private void MaybeEvictExpired(DateTime nowUtc)
    {
        var last = Interlocked.Read(ref _lastEvictionTicksUtc);
        if (nowUtc.Ticks - last < EvictionIntervalTicks) return;
        if (Interlocked.CompareExchange(ref _lastEvictionTicksUtc, nowUtc.Ticks, last) != last) return;

        EvictExpired(nowUtc);
    }

    /// <summary>Diagnostics/tests: proves the tracked state cannot grow without bound.</summary>
    public int TrackedPairCount => _perPair.Count;

    /// <summary>Diagnostics/tests: proves the tracked state cannot grow without bound.</summary>
    public int TrackedIpCount => _perIp.Count;

    private bool IsLive(DateTime windowStartUtc, DateTime nowUtc)
        => nowUtc - windowStartUtc < TimeSpan.FromMinutes(_options.FailureWindowMinutes);

    private void EvictExpired(DateTime nowUtc)
    {
        // Cheap opportunistic cleanup: drop entries whose windows died long
        // ago (2x the window) so long-running processes don't grow forever.
        var staleBefore = nowUtc.AddMinutes(-2 * _options.FailureWindowMinutes);

        foreach (var kvp in _perPair)
            if (kvp.Value.WindowStartUtc < staleBefore && kvp.Value.LockedUntilUtc < nowUtc)
                _perPair.TryRemove(kvp.Key, out _);

        foreach (var kvp in _perIp)
            if (kvp.Value.WindowStartUtc < staleBefore)
                _perIp.TryRemove(kvp.Key, out _);
    }
}

/// <summary>Static accessor helpers so the middleware can read the current options.</summary>
public static class LoginThrottleDefaults
{
    public static LoginThrottleOptions Default { get; } = new();
}
