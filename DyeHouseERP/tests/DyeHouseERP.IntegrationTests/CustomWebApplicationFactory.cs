using DyeHouseERP.Application.Auth.DTOs;
using System.Net.Http.Json;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Boots the real API in-process against a real SQL Server test database.
/// Requires SQL Server reachable at the connection string below (defaults
/// to the same instance docker-compose.yml brings up at the repo root) -
/// this is a genuine integration test, not an in-memory fake, because the
/// numbering engine (SqlDocumentNumberGenerator) uses SQL Server-specific
/// T-SQL (sp_getapplock) that has no in-memory equivalent.
///
/// Uses the "Testing" environment so Program.cs's Development-only
/// migrate+seed block does NOT run - this factory resets and seeds the
/// schema itself via EnsureCreated(), since no EF migrations exist yet
/// (see README "Honest gaps").
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DYEHOUSE_TEST_CONNECTION")
        ?? "Server=localhost,1433;Database=DyeHouseERP_IntegrationTests;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));
        });
    }

    /// <summary>
    /// Drops and recreates the schema fresh, seeds document sequences + the
    /// default admin user, and returns a bearer token ready to attach to an
    /// HttpClient. Call once per test class (see IntegrationTestBase).
    /// </summary>
    public async Task<string> ResetDatabaseAndLoginAsync(HttpClient client)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await DocumentSequenceSeeder.SeedAsync(db);
        await UserSeeder.SeedAsync(db, passwordHasher);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = UserSeeder.DefaultUsername,
            password = UserSeeder.DefaultPassword
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        return result!.Token;
    }
}
