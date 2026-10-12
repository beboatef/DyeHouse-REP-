namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// Serializes stock and money operations that must not interleave their
/// "read balance" and "write ledger" steps (spec section 18's negative-stock
/// rule). Implementations take a database-level application lock - in
/// Persistence this is sp_getapplock, the same transaction-scoped locking
/// pattern the document-numbering engine uses - so two concurrent requests for
/// the same stock dimension queue up instead of both passing the balance check
/// and jointly overselling it.
///
/// H6 widened this from "raw allocation only" to a system-wide stock lock:
/// every inventory/material posting path takes the same kind of lock, keyed by
/// the same <see cref="StockLockKey"/>, so the guarantee no longer depends on
/// which code path happens to move the stock.
///
/// Locking discipline for callers:
/// - Take the lock BEFORE the balance/validation read and hold it THROUGH
///   SaveChangesAsync (the guard is disposed after the save).
/// - A handler that touches several dimensions passes them all to
///   AcquireManyAsync, which sorts them so concurrent multi-dimension
///   handlers cannot deadlock each other.
/// - Never trust a validation that ran before the lock was taken.
/// </summary>
public interface IAllocationLockService
{
    /// <summary>
    /// Acquires the lock for one stock dimension; dispose to release it
    /// (with the owning connection, if one had to be opened).
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires every supplied dimension at once, in a deterministic order.
    /// Duplicates are collapsed. If any lock cannot be taken, the ones already
    /// taken are released before throwing, so a failed acquisition never
    /// leaves a partial hold behind.
    /// </summary>
    Task<IAsyncDisposable> AcquireManyAsync(
        IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a lock under a caller-chosen resource name, for documents that
    /// need serialization but are not stock (e.g. two receipts racing on the
    /// same invoice). The name is namespaced by the caller and must be unique
    /// per document.
    /// </summary>
    Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires an EXCLUSIVE application lock on a caller-chosen resource.
    /// Used by period Close/Reopen, which must serialize against every posting
    /// holding the SHARED period lock. Regular stock paths never call this -
    /// they hold Shared dimension locks via the methods above.
    /// </summary>
    Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a SHARED application lock on a caller-chosen resource.
    /// Used by the period posting guard: many postings may hold it at once in
    /// SQL Server terms, but a Close/Reopen holding EXCLUSIVE on the same
    /// resource excludes them all (and vice versa) until both sides commit.
    /// </summary>
    Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default);
}
