using DyeHouseERP.Application.Common.Monitoring;
using DyeHouseERP.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DyeHouseERP.API.Health;

/// <summary>
/// Readiness: the database schema matches the code. A deployment that forgot
/// <c>dotnet ef database update</c> starts, answers /health, and then fails
/// every query - this check turns that into a failed readiness probe instead.
/// Zero pending migrations is healthy; anything else is unhealthy.
/// </summary>
public sealed class MigrationHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MigrationHealthCheck> _logger;

    /// <summary>Initializes a new instance of the MigrationHealthCheck class.</summary>
    public MigrationHealthCheck(IServiceScopeFactory scopes, ILogger<MigrationHealthCheck> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Reports healthy when the readiness condition holds; degraded/unhealthy otherwise.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pending = await db.Database.GetPendingMigrationsAsync(timeout.Token);
            var pendingList = pending.ToList();

            if (pendingList.Count == 0)
                return HealthCheckResult.Healthy("The schema matches the code: no pending migration.");

            // A database created by EnsureCreated (development and the
            // integration suite) has schema but no __EFMigrationsHistory table,
            // so every migration looks "pending". That path is legitimate for
            // non-production databases and must not fail readiness - but it must
            // be labelled, so a production database can never hide behind it:
            // production bootstrap runs MigrateAsync, which always writes history.
            var historyExists = await db.Database
                .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
                .SingleAsync(timeout.Token);
            if (historyExists == 0)
            {
                var schemaExists = await db.Database
                    .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'Warehouses', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
                    .SingleAsync(timeout.Token);
                if (schemaExists == 1)
                    return HealthCheckResult.Healthy(
                        "Schema present without migrations history (EnsureCreated development/test database); " +
                        $"{pendingList.Count} migration(s) defined in code are not tracked here.");
            }

            return HealthCheckResult.Unhealthy(
                $"The schema is behind the code: {pendingList.Count} pending migration(s): " +
                string.Join(", ", pendingList));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventName: {EventName}. Readiness: the migration check failed.",
                LogEvents.DatabaseConnectionFailed);
            return HealthCheckResult.Unhealthy("The migration check failed.", ex);
        }
    }
}

/// <summary>
/// Readiness: the master data the business logic assumes is present. Postings
/// resolve the formation stage and the four system warehouses by identity, so a
/// database that is reachable and migrated but was never seeded (or was wiped)
/// would fail every posting with "not found" errors - this check fails the
/// readiness probe instead, pointing at the seed step.
/// </summary>
public sealed class SeedDataHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly string[] RequiredWarehouseCodes = ["WH-RAW", "WH-RDY", "WH-MAT", "WH-SUP"];

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SeedDataHealthCheck> _logger;

    /// <summary>Initializes a new instance of the SeedDataHealthCheck class.</summary>
    public SeedDataHealthCheck(IServiceScopeFactory scopes, ILogger<SeedDataHealthCheck> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }
/// <summary>Reports healthy when the readiness condition holds; degraded/unhealthy otherwise.</summary>

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var codes = await db.Warehouses.AsNoTracking()
                .Select(w => w.Code)
                .ToListAsync(timeout.Token);
            var missing = RequiredWarehouseCodes.Where(c => !codes.Contains(c)).ToList();

            var hasFormationStage = await db.ProductionStageDefinitions.AsNoTracking()
                .AnyAsync(s => s.IsFormationStage, timeout.Token);

            if (missing.Count == 0 && hasFormationStage)
                return HealthCheckResult.Healthy("The formation stage and the four system warehouses are present.");

            var reasons = new List<string>();
            if (missing.Count > 0)
                reasons.Add($"missing system warehouses: {string.Join(", ", missing)}");
            if (!hasFormationStage)
                reasons.Add("no formation stage is defined");
            return HealthCheckResult.Unhealthy("Seed data is incomplete: " + string.Join("; ", reasons) + ".");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventName: {EventName}. Readiness: the seed-data check failed.",
                LogEvents.DatabaseConnectionFailed);
            return HealthCheckResult.Unhealthy("The seed-data check failed.", ex);
        }
    }
}
