using System.Net.Http.Headers;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Base class for integration test classes: gets a fresh database and a
/// logged-in HttpClient once per test class (xUnit's IAsyncLifetime -
/// constructors can't be async, so setup lives in InitializeAsync).
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly CustomWebApplicationFactory Factory = new();
    protected HttpClient Client = null!;

    public async Task InitializeAsync()
    {
        Client = Factory.CreateClient();
        var token = await Factory.ResetDatabaseAndLoginAsync(Client);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
