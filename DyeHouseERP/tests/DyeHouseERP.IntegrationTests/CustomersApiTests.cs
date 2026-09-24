using System.Net;
using System.Net.Http.Json;
using DyeHouseERP.Application.Customers.DTOs;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

public class CustomersApiTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateCustomer_ThenListWithSearch_RoundTrips()
    {
        var code = $"C{Guid.NewGuid():N}"[..12];

        var createResponse = await Client.PostAsJsonAsync("/api/customers", new { code, name = "Acme Textiles" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await Client.GetAsync($"/api/customers?search={code}");
        listResponse.EnsureSuccessStatusCode();

        var customers = await listResponse.Content.ReadFromJsonAsync<List<CustomerDto>>();
        customers.Should().ContainSingle(c => c.Code == code && c.Name == "Acme Textiles");
    }

    [Fact]
    public async Task CreateCustomer_WithDuplicateCode_Returns409()
    {
        var code = $"C{Guid.NewGuid():N}"[..12];
        await Client.PostAsJsonAsync("/api/customers", new { code, name = "First" });

        var secondResponse = await Client.PostAsJsonAsync("/api/customers", new { code, name = "Second" });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Get_WithoutAuthToken_Returns401()
    {
        using var anonymousClient = Factory.CreateClient(); // no Authorization header attached

        var response = await anonymousClient.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
