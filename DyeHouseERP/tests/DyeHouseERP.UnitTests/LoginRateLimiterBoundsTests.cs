using DyeHouseERP.API.Auth;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The throttle is on the unauthenticated login endpoint, so its own memory use is attacker
/// controlled. Spraying unique usernames must not grow the dictionaries without bound.
/// </summary>
public class LoginRateLimiterBoundsTests
{
    [Fact]
    public void Unique_usernames_do_not_grow_the_tracked_pairs_without_bound()
    {
        var limiter = new LoginRateLimiter(new LoginThrottleOptions());
        var now = DateTime.UtcNow;

        for (var i = 0; i < 25_000; i++)
            limiter.RecordFailure("203.0.113.7", $"sprayed-user-{i}", now);

        Assert.True(limiter.TrackedPairCount <= 20_000,
            $"tracked pairs grew to {limiter.TrackedPairCount}");
        Assert.True(limiter.TrackedIpCount <= 10_000);
    }

    [Fact]
    public void Repeated_failures_for_one_key_still_lock_it_out()
    {
        var limiter = new LoginRateLimiter(new LoginThrottleOptions());
        var now = DateTime.UtcNow;

        for (var i = 0; i < 5; i++)
            limiter.RecordFailure("198.51.100.4", "real.user", now);

        Assert.True(limiter.IsLockedOut("198.51.100.4", "real.user", now));
    }
}
