using System.Net;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Part 3 (monitoring) probe contract: /health is anonymous liveness with no
/// checks; /health/ready demands the admin permission and reports the database,
/// migration and seed-data checks. Runs in CI against the integration SQL Server.
/// </summary>
[Collection("IntegrationDatabase")]
public class HealthProbeTests : IntegrationTestBase
{
    [Fact]
    public async Task Health_Is_Anonymous_Liveness()
    {
        using var anonymous = Factory.CreateClient();
        var response = await anonymous.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_Is_Forbidden_Anonymously()
    {
        using var anonymous = Factory.CreateClient();
        var response = await anonymous.GetAsync("/health/ready");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ready_Is_Healthy_For_Admin_On_Seeded_Database()
    {
        // Client carries the bootstrap admin token (users.manage): the seeded
        // database has the formation stage, the four system warehouses and no
        // pending migration, so readiness must be Healthy.
        var response = await Client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }
}
