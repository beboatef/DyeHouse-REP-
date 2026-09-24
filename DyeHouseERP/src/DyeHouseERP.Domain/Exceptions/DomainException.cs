namespace DyeHouseERP.Domain.Exceptions;

/// <summary>Base type for all business-rule violations raised from inside the Domain layer.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>
/// Raised when a transaction would take a customer/item/message balance negative
/// and no negative-stock override has been supplied. See spec section 17.
/// </summary>
public class NegativeStockException : DomainException
{
    public decimal CurrentBalance { get; }
    public decimal RequestedQuantity { get; }
    public decimal Shortage => RequestedQuantity - CurrentBalance;

    public NegativeStockException(decimal currentBalance, decimal requestedQuantity)
        : base($"Operation would result in negative stock. Current balance: {currentBalance}, requested: {requestedQuantity}, shortage: {requestedQuantity - currentBalance}.")
    {
        CurrentBalance = currentBalance;
        RequestedQuantity = requestedQuantity;
    }
}

/// <summary>Raised when a code that must be unique (customer code, item code, ...) already exists.</summary>
public class DuplicateCodeException : DomainException
{
    public DuplicateCodeException(string entityName, string code)
        : base($"{entityName} with code '{code}' already exists.") { }
}

/// <summary>Raised when an attempt is made to edit or void a finalized/locked document.</summary>
public class DocumentLockedException : DomainException
{
    public DocumentLockedException(string documentType, string documentNumber)
        : base($"{documentType} '{documentNumber}' is finalized and can no longer be edited directly. Create a reversal/adjustment instead.") { }
}
