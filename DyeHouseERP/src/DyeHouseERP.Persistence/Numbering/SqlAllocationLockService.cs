using System.Data;
using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Numbering;

/// <summary>
/// sp_getapplock-based serialization for the balance-check-then-post window,
/// using the same locking pattern as SqlDocumentNumberGenerator: an exclusive,
/// session-scoped app lock, held until dispose.
///
/// H6: the lock is no longer specific to raw allocation. Every inventory and
/// material posting path acquires the SAME kind of lock through this service,
/// keyed by the full stock dimension (warehouse, item, customer, production
/// order, raw message, material). Two writers that touch the same dimension
/// therefore serialize, whichever code path they live in; two writers on
/// different dimensions never block each other.
///
/// The lock is taken on the scoped DbContext's own connection - the handler's
/// subsequent queries and SaveChangesAsync run on that same open connection,
/// so the critical section genuinely covers the ledger write. A 15-second wait
/// fails loudly instead of queueing a slow request forever.
/// </summary>
public class SqlAllocationLockService : IAllocationLockService
{
    private const int LockTimeoutSeconds = 15;

    /// <summary>Namespace for caller-chosen, non-stock document locks.</summary>
    private const string NamedResourcePrefix = "Doc:";

    private readonly ApplicationDbContext _context;

    public SqlAllocationLockService(ApplicationDbContext context) => _context = context;

    public async Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
    {
        var connection = await OpenConnectionAsync(cancellationToken);
        var guard = await TryAcquireAsync(connection, key.Resource, cancellationToken);
        return guard ?? throw Timeout(key.Resource);
    }

    public async Task<IAsyncDisposable> AcquireManyAsync(
        IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
    {
        // Deterministic acquisition order: two handlers that lock several
        // dimensions in different orders would otherwise be able to deadlock.
        var resources = keys
            .Select(k => k.Resource)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        if (resources.Count == 0)
            return NoopLock.Instance;

        var connection = await OpenConnectionAsync(cancellationToken);
        var acquired = new List<SessionAppLockGuard>();

        try
        {
            foreach (var resource in resources)
            {
                var guard = await TryAcquireAsync(connection, resource, cancellationToken);
                if (guard is null)
                {
                    // Release what we already hold before failing, so a timeout
                    // on the second dimension cannot strand the first one.
                    foreach (var held in acquired) await held.DisposeAsync();
                    throw Timeout(resource);
                }

                acquired.Add(guard);
            }
        }
        catch
        {
            foreach (var held in acquired) await held.DisposeAsync();
            throw;
        }

        return new CompositeAppLockGuard(acquired);
    }

    public async Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("A lock resource name is required.", nameof(resource));

        var connection = await OpenConnectionAsync(cancellationToken);
        var fullResource = NamedResourcePrefix + resource.Trim();
        var guard = await TryAcquireAsync(connection, fullResource, cancellationToken);
        return guard ?? throw Timeout(fullResource);
    }

    /// <summary>
    /// R1: the legacy (rawMessage, item, customer) overload has been removed.
    /// Every stock path - allocation included - now locks through
    /// <see cref="AcquireAsync(StockLockKey, CancellationToken)"/>, so there is
    /// exactly one resource format per dimension and no path can silently fall
    /// back to a differently-named lock.
    /// </summary>

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>Returns null when the lock could not be taken within the timeout.</summary>
    private static async Task<SessionAppLockGuard?> TryAcquireAsync(
        SqlConnection connection, string resource, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", resource));
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        // Session-scoped: it survives across the handler's individual EF
        // commands and is released deterministically by sp_releaseapplock
        // in the guard's DisposeAsync.
        command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
        command.Parameters.Add(new SqlParameter("@LockTimeout", LockTimeoutSeconds * 1000));
        var returnParam = new SqlParameter("@ReturnValue", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(returnParam);

        await command.ExecuteNonQueryAsync(cancellationToken);

        var result = (int)returnParam.Value;
        return result < 0 ? null : new SessionAppLockGuard(connection, resource);
    }

    private static TimeoutException Timeout(string resource)
        => new($"Could not acquire the stock lock '{resource}' within {LockTimeoutSeconds}s " +
               $"(sp_getapplock returned a negative result). Another operation on the same stock is in progress - retry.");

    private sealed class CompositeAppLockGuard : IAsyncDisposable
    {
        private readonly IReadOnlyList<SessionAppLockGuard> _guards;
        private bool _released;

        public CompositeAppLockGuard(IReadOnlyList<SessionAppLockGuard> guards) => _guards = guards;

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            foreach (var guard in _guards) await guard.DisposeAsync();
        }
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public static readonly NoopLock Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SessionAppLockGuard : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private readonly string _resource;
        private bool _released;

        public SessionAppLockGuard(SqlConnection connection, string resource)
        {
            _connection = connection;
            _resource = resource;
        }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;

            try
            {
                await using var release = _connection.CreateCommand();
                release.CommandText = "sp_releaseapplock";
                release.CommandType = CommandType.StoredProcedure;
                release.Parameters.Add(new SqlParameter("@Resource", _resource));
                release.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
                await release.ExecuteNonQueryAsync();
            }
            catch
            {
                // The lock is session-scoped and the session dies with the
                // connection, so a failed explicit release still cannot leak
                // the lock beyond this request.
            }
        }
    }
}
