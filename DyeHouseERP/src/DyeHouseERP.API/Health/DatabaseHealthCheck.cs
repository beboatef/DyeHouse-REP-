using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DyeHouseERP.API.Health;

/// <summary>
/// Readiness probe: can this instance actually reach its database?
///
/// Why it exists: the API performs its migrations and seed before it starts listening, but a
/// database that goes away later (network, failover, wrong credentials after a config change) is
/// invisible from the outside - the process is up and every request fails. A load balancer or
/// container orchestrator needs one endpoint that answers that question without touching business
/// data, so this check only opens a connection and asks the server to reply.
///
/// It deliberately does NOT run a query against a table: a health probe must not take locks, and
/// "can connect" is the question a deployment is actually asking. A database that is reachable but
/// half-migrated is caught by the startup bootstrap, not by this probe.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    /// <summary>A hung database must not hang the probe: the caller is waiting on this answer.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(IServiceScopeFactory scopes, ILogger<DatabaseHealthCheck> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            // A fresh scope per probe: the DbContext is scoped and must not be shared with a request.
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var reachable = await db.Database.CanConnectAsync(timeout.Token);

            return reachable
                ? HealthCheckResult.Healthy("The database answered.")
                : HealthCheckResult.Unhealthy("The database did not answer.");
        }
        catch (Exception ex)
        {
            // Reported as unhealthy with the reason attached - never as a healthy process that
            // happens to be unable to do any work.
            _logger.LogError(ex, "Health check: the database could not be reached.");
            return HealthCheckResult.Unhealthy("The database could not be reached.", ex);
        }
    }
}
