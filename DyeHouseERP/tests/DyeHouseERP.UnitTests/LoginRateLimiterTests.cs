using DyeHouseERP.API.Auth;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// B3: the login throttle must lock only after repeated FAILED attempts,
/// clear on success, never punish ordinary traffic, and be safe under
/// concurrent calls.
/// </summary>
public class LoginRateLimiterTests
{
    private static LoginRateLimiter MakeLimiter(
        int maxFailed = 5, int windowMinutes = 15, int lockoutMinutes = 15, int perIp = 20)
        => new(new LoginThrottleOptions
        {
            MaxFailedAttempts = maxFailed,
            FailureWindowMinutes = windowMinutes,
            LockoutMinutes = lockoutMinutes,
            MaxFailedAttemptsPerIp = perIp
        });

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Failures_WithinLimit_AreNotLockedOut()
    {
        var limiter = MakeLimiter(maxFailed: 5);
        for (var i = 0; i < 4; i++)
            limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(i));

        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(5)).Should().BeFalse(
            "4 failures below the limit of 5 must still allow the next attempt through");
    }

    [Fact]
    public void ReachingTheLimit_LocksThePair()
    {
        var limiter = MakeLimiter(maxFailed: 5);
        for (var i = 0; i < 5; i++)
            limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(i));

        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(6)).Should().BeTrue();
    }

    [Fact]
    public void Lockout_Expires_AfterLockoutMinutes()
    {
        var limiter = MakeLimiter(maxFailed: 3, lockoutMinutes: 15);
        for (var i = 0; i < 3; i++)
            limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(i));

        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(3)).Should().BeTrue();
        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(3).AddMinutes(15).AddSeconds(1)).Should().BeFalse(
            "the lock must lift after LockoutMinutes so the user can try again");
    }

    [Fact]
    public void Lockout_IsScopedToThePair_NotTheWholeIpOrAllUsers()
    {
        var limiter = MakeLimiter(maxFailed: 3);
        for (var i = 0; i < 3; i++)
            limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(i));

        limiter.IsLockedOut("1.2.3.4", "warehouse-user", T0.AddMinutes(3)).Should().BeFalse(
            "another username from the same IP must not be dragged into the lockout");
        limiter.IsLockedOut("5.6.7.8", "admin", T0.AddMinutes(3)).Should().BeFalse(
            "the same username from another IP must not be locked by this pair alone");
    }

    [Fact]
    public void SuccessfulLogin_ClearsTheFailureCount()
    {
        var limiter = MakeLimiter(maxFailed: 3);
        limiter.RecordFailure("1.2.3.4", "admin", T0);
        limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(1));
        limiter.RecordSuccess("1.2.3.4", "admin");

        limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(2));

        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(3)).Should().BeFalse(
            "after a successful login the counter restarted, so one fresh failure is not a lockout");
    }

    [Fact]
    public void SuccessfulLogin_NeverCounts_AsPressure()
    {
        var limiter = MakeLimiter(maxFailed: 3);
        for (var i = 0; i < 10; i++)
            limiter.RecordSuccess("1.2.3.4", "admin");

        limiter.IsLockedOut("1.2.3.4", "admin", T0).Should().BeFalse(
            "successful logins must not contribute to the failure budget");
    }

    [Fact]
    public void PerIpDimension_LocksAfterManyFailures_AcrossUsernames()
    {
        var limiter = MakeLimiter(maxFailed: 100, perIp: 4); // pair dimension effectively off

        limiter.RecordFailure("9.9.9.9", "admin", T0);
        limiter.RecordFailure("9.9.9.9", "warehouse1", T0.AddSeconds(1));
        limiter.RecordFailure("9.9.9.9", "warehouse2", T0.AddSeconds(2));
        limiter.RecordFailure("9.9.9.9", "warehouse3", T0.AddSeconds(3));

        limiter.IsLockedOut("9.9.9.9", "victim", T0.AddSeconds(4)).Should().BeTrue(
            "one host spraying many usernames must be throttled by the IP dimension");
        limiter.IsLockedOut("8.8.8.8", "victim", T0.AddSeconds(4)).Should().BeFalse(
            "other IPs stay unaffected");
    }

    [Fact]
    public void WindowExpiry_RestartsTheFailureCount()
    {
        var limiter = MakeLimiter(maxFailed: 3, windowMinutes: 15);
        limiter.RecordFailure("1.2.3.4", "admin", T0);
        limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(14));

        // Third failure 20 minutes later: the earlier two have aged out.
        limiter.RecordFailure("1.2.3.4", "admin", T0.AddMinutes(20));

        limiter.IsLockedOut("1.2.3.4", "admin", T0.AddMinutes(20).AddSeconds(1)).Should().BeFalse(
            "only failures inside the live window count; old ones must not accumulate forever");
    }

    [Fact]
    public void PerIpDisabled_Zero_DisablesThatDimension()
    {
        var limiter = MakeLimiter(maxFailed: 100, perIp: 0);
        for (var i = 0; i < 50; i++)
            limiter.RecordFailure("9.9.9.9", $"user{i}", T0.AddSeconds(i));

        limiter.IsLockedOut("9.9.9.9", "anyone", T0.AddMinutes(1)).Should().BeFalse(
            "MaxFailedAttemptsPerIp=0 disables the IP-wide dimension");
    }

    [Fact]
    public async Task ConcurrentFailures_DoNotLoseCount()
    {
        var limiter = MakeLimiter(maxFailed: 20);
        var now = T0;

        // 40 parallel failures against a budget of 20 must end locked out;
        // a lost-update race would let some writes vanish.
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() =>
            limiter.RecordFailure("7.7.7.7", "admin", now))));

        limiter.IsLockedOut("7.7.7.7", "admin", now.AddSeconds(1)).Should().BeTrue();
    }
}
