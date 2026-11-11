using System.Security.Claims;
using DyeHouseERP.API.Middleware;
using DyeHouseERP.Application.Auth.Commands;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Part 2 (password/session policy) unit tests: the policy itself, forced-change creation,
/// self change (flag cleared, sessions revoked, reuse refused), admin reset (flag set),
/// expiry flagging without lockout, idle refresh refusal, and the middleware gate.
/// </summary>
public class PasswordPolicyTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PasswordPolicyTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"password-policy-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);
    private static PasswordHasher Hasher() => new();

    private static User SeedUser(ApplicationDbContext db, string username, string password)
    {
        var user = new User(username, Hasher().Hash(password), "Display", "users.manage", "seed");
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

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

    // ---- The policy itself ----

    [Fact]
    public void Short_Passwords_Are_Refused()
    {
        var errors = PasswordPolicy.Validate("sara", "Short1!x", null, Enumerable.Empty<string>(), Hasher());

        errors.Should().ContainSingle().Which.Should().Be(PasswordPolicy.TooShortMessage);
    }

    [Fact]
    public void Password_Containing_Username_Is_Refused_Case_Insensitive()
    {
        var errors = PasswordPolicy.Validate("sara", "My-SARA-pass-99!", null, Enumerable.Empty<string>(), Hasher());

        errors.Should().Contain(PasswordPolicy.ContainsUsernameMessage);
    }

    [Fact]
    public void Reuse_Of_Current_And_History_Passwords_Is_Refused()
    {
        var hasher = Hasher();
        var current = hasher.Hash("Current-Pass-99!");
        var older = hasher.Hash("Older-Pass-88!!");

        PasswordPolicy.Validate("sara", "Current-Pass-99!", current, new[] { older }, hasher)
            .Should().Contain(PasswordPolicy.ReusedMessage);
        PasswordPolicy.Validate("sara", "Older-Pass-88!!", current, new[] { older }, hasher)
            .Should().Contain(PasswordPolicy.ReusedMessage);
    }

    [Fact]
    public void Fresh_Password_Passes()
    {
        var hasher = Hasher();
        PasswordPolicy.Validate("sara", "Brand-New-Pass-1!", hasher.Hash("Current-Pass-99!"), new[] { hasher.Hash("Older-Pass-88!!") }, hasher)
            .Should().BeEmpty();
    }

    // ---- Creation, change, reset ----

    [Fact]
    public async Task Created_User_Starts_Flagged_With_History_And_Audit()
    {
        using var db = NewDb();
        var handler = new CreateUserCommandHandler(db, new StubCurrentUser("admin"), Hasher(), NullLogger<CreateUserCommandHandler>.Instance);

        var created = await handler.Handle(
            new CreateUserCommand("sara", "Fresh-Pass-1234!", "Sara", new List<string> { "users.manage" }),
            CancellationToken.None);

        created.MustChangePassword.Should().BeTrue();
        var user = await db.Users.SingleAsync(u => u.Username == "sara");
        user.MustChangePassword.Should().BeTrue();
        user.PasswordChangedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await db.PasswordHistories.CountAsync(h => h.UserId == user.Id)).Should().Be(1);
        (await db.AuditLogEntries.CountAsync(a => a.Action == "UserCreated")).Should().Be(1);
    }

    [Fact]
    public async Task Self_Change_Clears_Flag_Revokes_Sessions_And_Audits()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        var tokens = TokenService();
        var login = await new LoginCommandHandler(db, Hasher(), tokens.Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);
        login.RefreshToken.Should().NotBeNullOrEmpty();

        // Second session, so revocation has something to kill.
        var second = await new RefreshSessionCommandHandler(db, tokens.Object)
            .Handle(new RefreshSessionCommand(login.RefreshToken), CancellationToken.None);

        var self = new StubCurrentUser("sara");
        await new ChangeOwnPasswordCommandHandler(db, self, Hasher(), NullLogger<ChangeOwnPasswordCommandHandler>.Instance)
            .Handle(new ChangeOwnPasswordCommand("Old-Pass-12345!", "Brand-New-Pass-1!"), CancellationToken.None);

        var reloaded = await db.Users.SingleAsync(u => u.Username == "sara");
        reloaded.MustChangePassword.Should().BeFalse();
        (await db.UserRefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)).Should().Be(0);
        var audit = await db.AuditLogEntries.Where(a => a.Action == "PasswordChanged").ToListAsync();
        audit.Should().ContainSingle();
        audit[0].BeforeDataJson.Should().BeNull();
        audit[0].AfterDataJson.Should().BeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task Self_Change_With_Wrong_Current_Password_Fails()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Old-Pass-12345!");

        var act = () => new ChangeOwnPasswordCommandHandler(db, new StubCurrentUser("sara"), Hasher())
            .Handle(new ChangeOwnPasswordCommand("Wrong-Pass-0000!", "Brand-New-Pass-1!"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
    }

    [Fact]
    public async Task Admin_Reset_Sets_Flag_And_Kills_Sessions()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        var tokens = TokenService();
        var login = await new LoginCommandHandler(db, Hasher(), tokens.Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        await new ChangePasswordCommandHandler(db, new StubCurrentUser("admin"), Hasher(), NullLogger<ChangePasswordCommandHandler>.Instance)
            .Handle(new ChangePasswordCommand(user.Id, "Reset-Pass-9999!"), CancellationToken.None);

        (await db.Users.SingleAsync(u => u.Username == "sara")).MustChangePassword.Should().BeTrue();
        (await db.UserRefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)).Should().Be(0);
        (await db.AuditLogEntries.CountAsync(a => a.Action == "PasswordReset")).Should().Be(1);
        login.Should().NotBeNull();
    }

    // ---- Expiry: flag, never lock out ----

    [Fact]
    public async Task Expired_Password_Flags_But_Login_Still_Succeeds()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        // Age the password beyond the policy: FlagExpiredPassword is what login calls.
        user.FlagExpiredPassword("test");
        db.CompanySettings.Add(new CompanySettings("test"));
        await db.SaveChangesAsync();
        var settings = await db.CompanySettings.SingleAsync();
        settings.UpdatePasswordPolicy(30, 30, "test");
        await db.SaveChangesAsync();

        var result = await new LoginCommandHandler(db, Hasher(), TokenService().Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        result.Token.Should().NotBeNullOrEmpty();
        result.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Login_Never_Clears_An_Existing_Flag()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        user.MarkPasswordSetByAdministrator("test");
        await db.SaveChangesAsync();

        // Signing in again must not clear the flag: only a successful password change does.
        var result = await new LoginCommandHandler(db, Hasher(), TokenService().Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        result.MustChangePassword.Should().BeTrue();
        (await db.Users.SingleAsync(u => u.Username == "sara")).MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Expiry_Disabled_Never_Flags()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Old-Pass-12345!");
        db.CompanySettings.Add(new CompanySettings("test")); // defaults: max age 0 = disabled
        await db.SaveChangesAsync();

        var result = await new LoginCommandHandler(db, Hasher(), TokenService().Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        result.MustChangePassword.Should().BeFalse();
    }

    // ---- Idle timeout ----

    [Fact]
    public async Task Idle_Session_Refresh_Is_Refused()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Old-Pass-12345!");
        var tokens = TokenService();
        var first = await new LoginCommandHandler(db, Hasher(), tokens.Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        // The session idled past the 30-minute default: backdate the token row's activity.
        var stored = await db.UserRefreshTokens.SingleAsync();
        stored.Touch(DateTime.UtcNow.AddHours(-2));
        await db.SaveChangesAsync();

        var act = () => new RefreshSessionCommandHandler(db, tokens.Object)
            .Handle(new RefreshSessionCommand(first.RefreshToken), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCredentialsException>(act);
        (await db.UserRefreshTokens.SingleAsync()).RevokedReason.Should().Be("IdleTimeout");
    }

    [Fact]
    public async Task Active_Session_Refresh_Succeeds()
    {
        using var db = NewDb();
        SeedUser(db, "sara", "Old-Pass-12345!");
        var tokens = TokenService();
        var first = await new LoginCommandHandler(db, Hasher(), tokens.Object, new StubCurrentUser("sara"))
            .Handle(new LoginCommand("sara", "Old-Pass-12345!"), CancellationToken.None);

        var rotated = await new RefreshSessionCommandHandler(db, tokens.Object)
            .Handle(new RefreshSessionCommand(first.RefreshToken), CancellationToken.None);

        rotated.RefreshToken.Should().NotBeNullOrEmpty();
    }

    // ---- The bootstrap admin cannot skip ----

    [Fact]
    public async Task Bootstrap_Admin_Seed_Sets_The_Forced_Change_Flag()
    {
        using var db = NewDb();
        await UserSeeder.SeedAsync(db, Hasher());

        var admin = await db.Users.SingleAsync(u => u.Username == UserSeeder.DefaultUsername);
        admin.MustChangePassword.Should().BeTrue();
    }

    // ---- The middleware gate ----

    private static DefaultHttpContext FlaggedContext(ApplicationDbContext db, Guid userId, string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "sara")
        }, "test");
        http.User = new ClaimsPrincipal(identity);
        return http;
    }

    [Fact]
    public async Task Gate_Blocks_Business_Endpoints_While_Flagged()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        user.MarkPasswordSetByAdministrator("test");
        await db.SaveChangesAsync();

        var reached = false;
        Task Next(HttpContext ctx) { reached = true; return Task.CompletedTask; }

        var blocked = FlaggedContext(db, user.Id, "/api/customers");
        await new MustChangePasswordMiddleware(Next).InvokeAsync(blocked, db);
        blocked.Response.StatusCode.Should().Be(403);
        reached.Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/auth/change-password")]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/health/ready")]
    public async Task Gate_Lets_Escape_Hatches_Through(string path)
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");
        user.MarkPasswordSetByAdministrator("test");
        await db.SaveChangesAsync();

        var reached = false;
        Task Next(HttpContext ctx) { reached = true; return Task.CompletedTask; }

        await new MustChangePasswordMiddleware(Next).InvokeAsync(FlaggedContext(db, user.Id, path), db);
        reached.Should().BeTrue();
    }

    [Fact]
    public async Task Gate_Passes_Unflagged_Users()
    {
        using var db = NewDb();
        var user = SeedUser(db, "sara", "Old-Pass-12345!");

        var reached = false;
        Task Next(HttpContext ctx) { reached = true; return Task.CompletedTask; }

        await new MustChangePasswordMiddleware(Next).InvokeAsync(FlaggedContext(db, user.Id, "/api/customers"), db);
        reached.Should().BeTrue();
    }
}
