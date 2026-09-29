using System.Data;
using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Numbering;

/// <summary>
/// sp_getapplock-based serialization for the balance-check-then-post window,
/// using the same locking pattern as SqlDocumentNumberGenerator: an exclusive,
/// session-scoped app lock keyed by (RawMessage, Item, Customer), held until
/// dispose. The allocation handler does its read + SaveChanges while the lock
/// is held, so two concurrent allocations for the same stock dimension cannot
/// both pass the negative-stock check (spec sections 13 and 18).
///
/// The lock is taken on the scoped DbContext's own connection - the handler's
/// subsequent queries and SaveChangesAsync run on that same open connection,
/// so the critical section genuinely covers the ledger write. A 15-second
/// wait fails loudly instead of queueing a slow request forever.
/// </summary>
public class SqlAllocationLockService : IAllocationLockService
{
    private const int LockTimeoutSeconds = 15;

    private readonly ApplicationDbContext _context;

    public SqlAllocationLockService(ApplicationDbContext context) => _context = context;

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid rawMessageId, Guid itemId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var lockResource = $"Alloc:{rawMessageId}:{itemId}:{customerId}";

        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", lockResource));
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
        if (result < 0)
            throw new TimeoutException(
                $"Could not acquire the allocation lock for message '{rawMessageId}' within {LockTimeoutSeconds}s (sp_getapplock returned {result}). Another allocation on the same material may be in progress - retry.");

        return new SessionAppLockGuard(connection, lockResource);
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
