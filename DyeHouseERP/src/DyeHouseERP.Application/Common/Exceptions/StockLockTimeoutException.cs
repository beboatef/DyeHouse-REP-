namespace DyeHouseERP.Application.Common.Exceptions;

/// <summary>
/// Thrown by <c>SqlAllocationLockService</c> when a stock/document lock
/// (sp_getapplock) could not be acquired within the wait budget because
/// another operation holds the same stock dimension. Mapped to HTTP 409
/// LOCK_TIMEOUT by ExceptionHandlingMiddleware - deliberately a distinct
/// type so unrelated timeouts (e.g. the document-numbering lock) are NOT
/// reported as stock conflicts.
/// </summary>
public class StockLockTimeoutException : TimeoutException
{
    public string LockResource { get; }

    public StockLockTimeoutException(string lockResource, string message)
        : base(message)
        => LockResource = lockResource;
}
