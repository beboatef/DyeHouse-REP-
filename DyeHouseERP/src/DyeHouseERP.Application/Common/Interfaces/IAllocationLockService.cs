namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// Serializes stock operations that must not interleave their
/// "read balance" and "write ledger" steps (spec section 18's negative-stock
/// rule). Implementations take a database-level lock keyed by the dimension
/// tuple - in Persistence this is sp_getapplock, the same transaction-scoped
/// locking pattern the document-numbering engine uses - so two concurrent
/// requests for the same (rawMessage, item, customer) queue up instead of
/// both passing the balance check and jointly overselling it.
/// </summary>
public interface IAllocationLockService
{
    /// <summary>Acquires the lock; dispose to release (with the owning transaction, if one was opened).</summary>
    Task<IAsyncDisposable> AcquireAsync(
        Guid rawMessageId, Guid itemId, Guid customerId, CancellationToken cancellationToken = default);
}
