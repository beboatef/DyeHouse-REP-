using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// Generates the next visible document number for a given document type,
/// concurrency-safe (spec section 6). Implemented in Infrastructure using a
/// locked SQL Server transaction (UPDLOCK, HOLDLOCK) - never "MAX(number)+1".
/// </summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(DocumentType documentType, Guid? warehouseId = null, CancellationToken cancellationToken = default);
}

/// <summary>Abstraction over "who is calling right now" - backed by the JWT/auth context in Infrastructure.</summary>
public interface ICurrentUserService
{
    string UserName { get; }
    Guid? UserId { get; }
    string? IpAddress { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
}

/// <summary>Testable clock abstraction - never call DateTime.Now/UtcNow directly from Application handlers.</summary>
public interface IDateTime
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Computes balances by summing the append-only InventoryTransaction ledger
/// (spec section 18) - the only way any handler should ever learn a
/// "current balance". Balances are tracked per (RawMessage, Item, Customer)
/// so that Customer-to-Customer Transfers (spec section 15) can move
/// ownership of part of a message without ever editing the message's
/// original CustomerId - see GetCustomerBalanceAsync.
/// </summary>
public interface IInventoryLedgerService
{
    /// <summary>Total remaining quantity in a message/item, regardless of which customer currently owns which portion.</summary>
    Task<(decimal Kg, decimal Meter)> GetMessageBalanceAsync(Guid rawMessageId, Guid itemId, Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The portion of a message/item currently owned by ONE specific
    /// customer - i.e. what that customer is actually allowed to consume,
    /// release, or transfer onward. A receipt posts its IN row under the
    /// receiving customer; a CustomerTransfer posts an OUT under the
    /// sending customer and an IN under the receiving one on the very same
    /// message, so this balance shifts between customers without the
    /// message's header ever being edited.
    /// </summary>
    Task<(decimal Kg, decimal Meter)> GetCustomerBalanceAsync(Guid rawMessageId, Guid itemId, Guid customerId, Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>All distinct RawMessageIds a customer currently has (or has ever had) any ledger activity against - i.e. originally received by them, or transferred to them.</summary>
    Task<List<Guid>> GetMessageIdsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>Balance lookups over the materials ledger (MaterialTransaction) - the materials equivalent of IInventoryLedgerService.</summary>
public interface IMaterialLedgerService
{
    Task<decimal> GetBalanceAsync(Guid materialId, Guid warehouseId, CancellationToken cancellationToken = default);
}

/// <summary>PBKDF2-based password hashing - see Infrastructure.Services.PasswordHasher.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

/// <summary>Issues signed JWTs for authenticated users - see Infrastructure.Services.JwtTokenService.</summary>
public interface ITokenService
{
    string GenerateToken(Guid userId, string username, IEnumerable<string> roles, out DateTime expiresAtUtc);
}

/// <summary>
/// Generic tabular export used by every "report" screen in the spec
/// (customer statement PDF/Excel, negative-stock override report, etc.) -
/// one implementation in Infrastructure backs every report instead of each
/// feature reinventing PDF/Excel generation.
/// </summary>
public interface IReportExportService
{
    byte[] GeneratePdf(string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows);
    byte[] GenerateExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows);
}

/// <summary>
/// Reads an uploaded Excel file into plain rows of cell text (spec section
/// 38 "Excel Import"). Deliberately dumb - it does not know about
/// Customers or Items or validation; each import command maps the row
/// dictionaries to its own DTO and validates them. Row 1 is assumed to be
/// the header row and becomes the dictionary keys.
/// </summary>
public interface IExcelReaderService
{
    List<Dictionary<string, string>> ReadRows(byte[] fileBytes, string sheetName = "");
}

/// <summary>Result of one row in an Excel import - either accepted for commit or rejected with a reason (spec section 38 "No silent invalid imports").</summary>
public class ImportRowResult
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
}

/// <summary>Reads rows out of an uploaded Excel file as plain header->value dictionaries - the actual entity mapping/validation stays in the Application command that calls this.</summary>
public interface IExcelImportReader
{
    List<Dictionary<string, string?>> ReadRows(Stream fileStream, out List<string> headers);
}

/// <summary>
/// Checks whether a date falls inside a closed accounting/inventory period
/// (spec section 43). Command handlers that post financial or inventory
/// documents should call EnsureOpenAsync before writing - see
/// IssueInvoiceCommand, CreateStockAdjustmentCommand, CreateReceiptCommand,
/// and CreatePaymentCommand for the pattern; extending it to every other
/// posting command is the same one-line addition.
/// </summary>
public interface IPeriodCloseService
{
    Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default);
}

