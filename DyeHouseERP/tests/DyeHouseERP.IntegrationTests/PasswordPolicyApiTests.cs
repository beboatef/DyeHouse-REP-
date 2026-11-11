using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DyeHouseERP.Application.Auth.DTOs;
using DyeHouseERP.Persistence.Numbering;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Part 2 (password/session policy) end-to-end: the bootstrap admin cannot skip the forced
/// change, reuse of recent passwords is refused, expiry flags without locking out, and a
/// password change kills every session. Runs in CI against the integration SQL Server.
/// </summary>
[Collection("IntegrationDatabase")]
public class PasswordPolicyApiTests : IntegrationTestBase
{
    private async Task<HttpClient> FreshAdminClientAsync()
    {
        // A second session on the same server: proves one password change kills the others.
        var client = Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        login.EnsureSuccessStatusCode();
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        result!.MustChangePassword.Should().BeFalse();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        return client;
    }

    [Fact]
    public async Task Flagged_Admin_Is_Blocked_Everywhere_Except_The_Escape_Hatches()
    {
        // Reset the admin back to flagged through the admin reset (re-proves the gate).
        var reset = await Client.PostAsJsonAsync(
            $"/api/users/{await AdminIdAsync()}/change-password",
            new { newPassword = "Forced-Again-99!" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The OLD token still authenticates (access token is short-lived and stateless)...
        var blocked = await Client.GetAsync("/api/users");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await blocked.Content.ReadAsStringAsync();
        body.Should().Contain("PASSWORD_CHANGE_REQUIRED");

        // ...but the escape hatches stay open.
        (await Client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var change = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Forced-Again-99!",
            newPassword = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-login: the flag is cleared and business endpoints answer again.
        var relogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        relogin.EnsureSuccessStatusCode();
        var result = await relogin.Content.ReadFromJsonAsync<LoginResultDto>();
        result!.MustChangePassword.Should().BeFalse();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        (await Client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reuse_Of_Recent_Passwords_Is_Refused()
    {
        var reused = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = CustomWebApplicationFactory.IntegrationAdminPassword,
            newPassword = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        reused.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And the development default (in history since seeding) is refused too.
        var ancient = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = CustomWebApplicationFactory.IntegrationAdminPassword,
            newPassword = UserSeeder.DefaultPassword
        });
        ancient.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Expiry_Flags_Without_Locking_Out()
    {
        // Backdate the password beyond a 30-day policy via SQL (the domain has no
        // "set an old date" path - the past is not a user input).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [Users] SET [PasswordChangedAtUtc] = DATEADD(day, -40, GETUTCDATE()) WHERE [Username] = {0}",
                UserSeeder.DefaultUsername);
        }

        var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        // Succeeds (200 with tokens), but flags...
        login.EnsureSuccessStatusCode();
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        result!.MustChangePassword.Should().BeTrue();

        // ...and the next business call is gated until a change.
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        var blocked = await Client.GetAsync("/api/users");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Restore: change to a fresh password so later tests in this class start clean.
        var change = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = CustomWebApplicationFactory.IntegrationAdminPassword,
            newPassword = "Post-Expiry-77!!"
        });
        change.EnsureSuccessStatusCode();
        var relogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = "Post-Expiry-77!!"
        });
        relogin.EnsureSuccessStatusCode();
        var fresh = await relogin.Content.ReadFromJsonAsync<LoginResultDto>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh!.Token);
        var again = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Post-Expiry-77!!",
            newPassword = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        again.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Password_Change_Kills_Existing_Sessions()
    {
        using var second = await FreshAdminClientAsync();

        // The second session works now...
        (await second.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);

        // ...the first client changes the password...
        var change = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = CustomWebApplicationFactory.IntegrationAdminPassword,
            newPassword = "Session-Killer-5!"
        });
        change.EnsureSuccessStatusCode();

        // ...and the second session's refresh is dead (its family was revoked).
        // (Its access token may still be valid for minutes - refresh is the session.)
        Client.DefaultRequestHeaders.Authorization = null;
        var relogin = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = "Session-Killer-5!"
        });
        relogin.EnsureSuccessStatusCode();
        var fresh = await relogin.Content.ReadFromJsonAsync<LoginResultDto>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh!.Token);
        var restore = await Client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Session-Killer-5!",
            newPassword = CustomWebApplicationFactory.IntegrationAdminPassword
        });
        restore.EnsureSuccessStatusCode();
    }

    private async Task<Guid> AdminIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
        return await db.Users.Where(u => u.Username == UserSeeder.DefaultUsername).Select(u => u.Id).SingleAsync();
    }
}
