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
/// Requires SQL Server reachable at the connection string supplied through
/// the DYEHOUSE_TEST_CONNECTION environment variable (there is no embedded
/// fallback credential) - this is a genuine integration test, not an
/// in-memory fake, because the
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
    /// <summary>
    /// The SQL Server connection string for the integration suite. It is
    /// REQUIRED - there is deliberately no embedded credential fallback, so no
    /// password is ever committed. It is read lazily and only when a test
    /// actually starts the factory, so simply referencing the type does not
    /// throw.
    /// </summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DYEHOUSE_TEST_CONNECTION")
        ?? throw new InvalidOperationException(
            "The integration tests need a real SQL Server test database. Set the environment variable " +
            "'DYEHOUSE_TEST_CONNECTION' to a connection string (for example " +
            "'Server=localhost,1433;Database=DyeHouseERP_IntegrationTests;Integrated Security=True;TrustServerCertificate=True;') " +
            "and run the integration tests again.");

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
