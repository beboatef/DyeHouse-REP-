using System.Security.Cryptography;
using DyeHouseERP.Application.Auth.Commands;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The refresh half of the session: a short-lived access token plus a rotating opaque refresh
/// token whose hash alone is stored. These tests pin the rotation contract - a token is single
/// use, reuse burns the family, and logout kills everything.
/// </summary>
public class RefreshSessionCommandTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public RefreshSessionCommandTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"refresh-session-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static Mock<ITokenService> TokenService()
    {
        var mock = new Mock<ITokenService>();
        mock.SetupGet(s => s.RefreshTokenLifetime).Returns(TimeSpan.FromDays(7));
        mock.SetupGet(s => s.RefreshReuseGrace).Returns(TimeSpan.FromSeconds(30));
        DateTime expires = DateTime.UtcNow.AddMinutes(30);
        mock.Setup(s => s.GenerateToken(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), out expires))
            .Returns("access-token");
        return mock;
    }

    private static User SeedUser(ApplicationDbContext db, string username, string password, bool active = true)
    {
        var user = new User(username, new PasswordHasher().Hash(password), "Display", "admin", "seed");
        if (!active) user.Deactivate("seed");
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static async Task<string> LoginAsync(ApplicationDbContext db, Mock<ITokenService> tokens, string username, string password)
    {
        var currentUser = new Mock<ICurrentUserService>();
        var result = await new LoginCommandHandler(db, new PasswordHasher(), tokens.Object, currentUser.Object)
            .Handle(new LoginCommand(username, password), CancellationToken.None);
        return result.RefreshToken;
    }

    private static RefreshSessionCommandHandler Refresher(ApplicationDbContext db, Mock<ITokenService> tokens)
        => new(db, tokens.Object);

    [Fact]
    public async Task Login_mints_a_refresh_token_whose_hash_only_is_stored()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();

        var refreshToken = await LoginAsync(db, tokens, "sara", "Secret-123");

        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        var stored = await db.UserRefreshTokens.SingleAsync();
        Assert.NotEqual(refreshToken, stored.TokenHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken))), stored.TokenHash);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_dies()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        var first = await LoginAsync(db, tokens, "sara", "Secret-123");

        var rotated = await Refresher(db, tokens).Handle(new RefreshSessionCommand(first), CancellationToken.None);

        Assert.NotEqual(first, rotated.RefreshToken);
        Assert.Equal(2, await db.UserRefreshTokens.CountAsync());
        var oldRow = await db.UserRefreshTokens.SingleAsync(t => t.ReplacedByTokenHash != null);
        Assert.Equal("Rotated", oldRow.RevokedReason);

        // The new token works for the next rotation.
        var again = await Refresher(db, tokens).Handle(new RefreshSessionCommand(rotated.RefreshToken), CancellationToken.None);
        Assert.NotEqual(rotated.RefreshToken, again.RefreshToken);
        Assert.Equal(3, await db.UserRefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_window_revokes_the_whole_family()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        // The grace is what makes an immediate replay benign; with it off this is the theft case.
        tokens.SetupGet(s => s.RefreshReuseGrace).Returns(TimeSpan.Zero);
        var first = await LoginAsync(db, tokens, "sara", "Secret-123");
        var rotated = await Refresher(db, tokens).Handle(new RefreshSessionCommand(first), CancellationToken.None);

        // The attacker replays the stolen first token after the legitimate client rotated it.
        var act = () => Refresher(db, tokens).Handle(new RefreshSessionCommand(first), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidCredentialsException>(act);

        Assert.Empty(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
        Assert.Contains(await db.AuditLogEntries.Select(a => a.Action).ToListAsync(), a => a == "RefreshTokenReuseDetected");

        // Even the legitimate newest token is dead now.
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => Refresher(db, tokens).Handle(new RefreshSessionCommand(rotated.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_and_inactive_cases_are_refused()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var leaver = SeedUser(db, "leaver", "Secret-123");
        var tokens = TokenService();
        var refresher = Refresher(db, tokens);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => refresher.Handle(new RefreshSessionCommand("not-a-real-token"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => refresher.Handle(new RefreshSessionCommand(""), CancellationToken.None));

        // The session outlives the account: the user is deactivated after signing in.
        var leaverToken = await LoginAsync(db, tokens, "leaver", "Secret-123");
        leaver.Deactivate("test");
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => refresher.Handle(new RefreshSessionCommand(leaverToken), CancellationToken.None));
    }

    [Fact]
    public async Task An_expired_token_is_refused_without_burning_the_family()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        tokens.SetupGet(s => s.RefreshTokenLifetime).Returns(TimeSpan.FromMilliseconds(1));
        var refresher = Refresher(db, tokens);

        var shortLived = await LoginAsync(db, tokens, "sara", "Secret-123");
        await Task.Delay(50);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => refresher.Handle(new RefreshSessionCommand(shortLived), CancellationToken.None));

        var row = await db.UserRefreshTokens.SingleAsync();
        Assert.Equal("Expired", row.RevokedReason);
        Assert.DoesNotContain(await db.AuditLogEntries.Select(a => a.Action).ToListAsync(), a => a == "RefreshTokenReuseDetected");
    }

    [Fact]
    public async Task Logout_revokes_every_live_token_of_the_user()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        await LoginAsync(db, tokens, "sara", "Secret-123");
        await LoginAsync(db, tokens, "sara", "Secret-123");
        Assert.Equal(2, await db.UserRefreshTokens.CountAsync(t => t.RevokedAtUtc == null));

        var revoked = await new RevokeUserRefreshTokensCommandHandler(db)
            .Handle(new RevokeUserRefreshTokensCommand(user.Id, "Logout"), CancellationToken.None);

        Assert.Equal(2, revoked);
        Assert.Empty(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task A_concurrent_tab_replaying_the_just_rotated_token_is_served_instead_of_logged_out()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        var shared = await LoginAsync(db, tokens, "sara", "Secret-123");

        // Tab A wins the race and rotates the cookie the two tabs were both holding.
        var winner = await Refresher(db, tokens).Handle(new RefreshSessionCommand(shared), CancellationToken.None);

        // Tab B's request left before that response landed, so it presents the same old token.
        var loser = await Refresher(db, tokens).Handle(new RefreshSessionCommand(shared), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(loser.Token));
        // No second refresh token: the winner already set that cookie.
        Assert.Equal(string.Empty, loser.RefreshToken);
        Assert.Equal(2, await db.UserRefreshTokens.CountAsync());

        // The family is intact - the tab that rotated keeps working.
        var next = await Refresher(db, tokens).Handle(new RefreshSessionCommand(winner.RefreshToken), CancellationToken.None);
        Assert.NotEqual(winner.RefreshToken, next.RefreshToken);

        var actions = await db.AuditLogEntries.Select(a => a.Action).ToListAsync();
        Assert.Contains(actions, a => a == "RefreshTokenReuseWithinGrace");
        Assert.DoesNotContain(actions, a => a == "RefreshTokenReuseDetected");
    }

    [Fact]
    public async Task The_grace_window_cannot_revive_a_session_that_was_already_closed()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        var shared = await LoginAsync(db, tokens, "sara", "Secret-123");
        await Refresher(db, tokens).Handle(new RefreshSessionCommand(shared), CancellationToken.None);

        // Logout kills the successor, so the replayed token has no live chain behind it.
        await new RevokeUserRefreshTokensCommandHandler(db)
            .Handle(new RevokeUserRefreshTokensCommand(user.Id, "Logout"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => Refresher(db, tokens).Handle(new RefreshSessionCommand(shared), CancellationToken.None));
    }

    [Fact]
    public async Task Logout_by_refresh_cookie_revokes_the_live_session_when_the_access_token_expired()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        var live = await LoginAsync(db, tokens, "sara", "Secret-123");

        var revoked = await new RevokeSessionByRefreshTokenCommandHandler(db)
            .Handle(new RevokeSessionByRefreshTokenCommand(live, "Logout"), CancellationToken.None);

        Assert.Equal(1, revoked);
        Assert.Empty(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());

        // An unknown or empty cookie is a silent no-op, never an error: the browser still clears.
        Assert.Equal(0, await new RevokeSessionByRefreshTokenCommandHandler(db)
            .Handle(new RevokeSessionByRefreshTokenCommand("not-a-real-token", "Logout"), CancellationToken.None));
        Assert.Equal(0, await new RevokeSessionByRefreshTokenCommandHandler(db)
            .Handle(new RevokeSessionByRefreshTokenCommand(string.Empty, "Logout"), CancellationToken.None));
    }

    [Fact]
    public async Task Logout_by_an_old_cookie_ends_the_session_its_rotation_created()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Secret-123");
        var tokens = TokenService();
        var before = await LoginAsync(db, tokens, "sara", "Secret-123");
        await Refresher(db, tokens).Handle(new RefreshSessionCommand(before), CancellationToken.None);

        // The cookie the browser still shows in a stale tab is the superseded one.
        var revoked = await new RevokeSessionByRefreshTokenCommandHandler(db)
            .Handle(new RevokeSessionByRefreshTokenCommand(before, "Logout"), CancellationToken.None);

        Assert.Equal(1, revoked);
        Assert.Empty(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
    }
}
