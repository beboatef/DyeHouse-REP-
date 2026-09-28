using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Commands;

/// <summary>
/// Excel import for Customers, following the safe workflow spec section 38
/// requires: Upload -> Read -> Validate -> Preview -> Show Errors ->
/// Confirm -> Execute -> Results.
///
/// Expected columns: Code, Name, AccountNumber, IsActive (header names are
/// matched case-insensitively and Arabic aliases are accepted - see
/// <see cref="ImportSheet.Value"/>), so the same template works for an
/// Arabic-speaking clerk and for an export from another system.
///
/// `allowUpdate` is the permission-controlled half of the feature. It is never
/// read from the request body: the API only routes to the update-capable
/// command from an action guarded by `customers.edit`, so a create-only user
/// physically cannot reach it.
///
/// Columns that would change a customer's *identity* are never rewritten from a
/// spreadsheet: Code is the join key to every downstream document and Account
/// Number mirrors it. Only descriptive fields (Name, IsActive) can be updated,
/// which is what makes "no silent overwrite" true rather than aspirational.
/// </summary>
public record PreviewCustomerImportCommand(byte[] FileBytes, bool AllowUpdate) : IRequest<ImportPreviewDto>;

public class PreviewCustomerImportCommandHandler : IRequestHandler<PreviewCustomerImportCommand, ImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewCustomerImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader)
    { _db = db; _reader = reader; }

    public async Task<ImportPreviewDto> Handle(PreviewCustomerImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await CustomerImportValidator.ValidateAsync(request.FileBytes, _db, _reader, request.AllowUpdate, cancellationToken);
        return new ImportPreviewDto
        {
            TotalRows = rows.Count,
            ValidRows = rows.Count(r => r.IsValid),
            InvalidRows = rows.Count(r => !r.IsValid),
            NewRows = rows.Count(r => r.IsValid && !r.IsExisting),
            ExistingRows = rows.Count(r => r.IsValid && r.IsExisting),
            UpdateExistingAllowed = request.AllowUpdate,
            Rows = rows
        };
    }
}

/// <summary>
/// The "Confirm -> Execute -> Results" step. Re-validates the same bytes from
/// scratch (never trusts a stale client-side preview) and commits only rows
/// that are still valid at commit time - so a row that became a duplicate
/// between preview and confirm is safely skipped, not silently double-created.
/// </summary>
public record ExecuteCustomerImportCommand(byte[] FileBytes, bool AllowUpdate) : IRequest<ImportExecuteResultDto>;

public class ExecuteCustomerImportCommandHandler : IRequestHandler<ExecuteCustomerImportCommand, ImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteCustomerImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    { _db = db; _reader = reader; _currentUser = currentUser; }

    public async Task<ImportExecuteResultDto> Handle(ExecuteCustomerImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await CustomerImportValidator.ValidateAsync(request.FileBytes, _db, _reader, request.AllowUpdate, cancellationToken);

        var existing = await _db.Customers.ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var created = 0;
        var updated = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            var code = row.Values.GetValueOrDefault("Code") ?? string.Empty;

            if (!row.IsValid)
            {
                errors.Add($"Row {row.RowNumber} ({code}): {string.Join("; ", row.Errors)}");
                row.Action = "Skip";
                continue;
            }

            if (row.IsExisting)
            {
                if (!existing.TryGetValue(code, out var customer))
                {
                    // It existed at preview time but not at commit time - deleted meanwhile.
                    errors.Add($"Row {row.RowNumber} ({code}): customer no longer exists; re-run the import.");
                    row.Action = "Skip";
                    continue;
                }

                ApplyUpdate(customer, row, _currentUser.UserName);
                row.Action = "Update";
                updated++;
                continue;
            }

            var newCustomer = new Customer(code, row.Values.GetValueOrDefault("Name")!, _currentUser.UserName);
            var accountNumber = row.Values.GetValueOrDefault("AccountNumber");
            if (!string.IsNullOrWhiteSpace(accountNumber) && !string.Equals(accountNumber, code, StringComparison.OrdinalIgnoreCase))
                newCustomer.SetAccountNumber(accountNumber!);
            if (!CustomerImportValidator.IsActive(row, defaultValue: true))
                newCustomer.Deactivate(_currentUser.UserName);

            _db.Customers.Add(newCustomer);
            row.Action = "Create";
            created++;
        }

        if (created + updated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new ImportExecuteResultDto
        {
            Created = created,
            Updated = updated,
            Skipped = rows.Count(r => !r.IsValid),
            Errors = errors,
            Rows = rows
        };
    }

    /// <summary>Only the descriptive fields. Code and AccountNumber are identity, not data.</summary>
    private static void ApplyUpdate(Customer customer, ImportRowPreview row, string userName)
    {
        customer.SetName(row.Values.GetValueOrDefault("Name")!);

        if (CustomerImportValidator.IsActive(row, defaultValue: customer.IsActive))
            customer.Activate(userName);
        else
            customer.Deactivate(userName);
    }
}

/// <summary>The downloadable template - the exact columns the validator reads (spec section 38, step 1).</summary>
public static class CustomerImportTemplate
{
    public static readonly string[] Headers = { "Code", "Name", "AccountNumber", "IsActive" };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[] { "CUS-001", "عميل تجريبي", "CUS-001", true },
        new object?[] { "CUS-002", "Sample Customer", "CUS-002", true }
    };
}

/// <summary>Shared row-level validation so Preview and Execute can never disagree about what's valid.</summary>
internal static class CustomerImportValidator
{
    private static readonly string[] CodeHeaders = { "Code", "كود", "Customer Code", "كود العميل" };
    private static readonly string[] NameHeaders = { "Name", "اسم", "Customer Name", "اسم العميل" };
    private static readonly string[] AccountHeaders = { "AccountNumber", "Account Number", "رقم الحساب" };
    private static readonly string[] ActiveHeaders = { "IsActive", "Active", "نشط" };

    public static async Task<List<ImportRowPreview>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader, bool allowUpdate, CancellationToken cancellationToken)
    {
        var rows = ImportSheet.Read(fileBytes, reader, out var headers);

        var existingCodes = (await db.Customers.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowPreview>();
        var rowNumber = 1; // row 1 is the header row, data starts at 2

        foreach (var raw in rows)
        {
            rowNumber++;
            var code = ImportSheet.Value(raw, headers, CodeHeaders);
            var name = ImportSheet.Value(raw, headers, NameHeaders);
            var accountNumber = ImportSheet.Value(raw, headers, AccountHeaders);
            var isActive = IsActive(raw, headers, defaultValue: true);

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(code)) errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(name)) errors.Add("Name is required.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (existingCodes.Contains(code)) errors.Add($"Code '{code}' already exists.");
                else if (!seenInFile.Add(code)) errors.Add($"Code '{code}' is duplicated within the file.");
            }

            var exists = !string.IsNullOrWhiteSpace(code) && existingCodes.Contains(code);
            if (exists && !allowUpdate)
                errors.Add("Customer already exists and you do not have permission to update it.");

            results.Add(new ImportRowPreview
            {
                RowNumber = rowNumber,
                IsValid = errors.Count == 0,
                IsExisting = exists,
                Action = exists ? "Update" : "Create",
                Errors = errors,
                Values = new Dictionary<string, string?>
                {
                    ["Code"] = code,
                    ["Name"] = name,
                    ["AccountNumber"] = accountNumber ?? code,
                    ["IsActive"] = isActive ? "true" : "false"
                }
            });
        }

        return results;
    }

    /// <summary>Accepts true/false, 1/0, yes/no and the Arabic نعم/لا in any case.</summary>
    public static bool IsActive(ImportRowPreview row, bool defaultValue)
    {
        var raw = row.Values.GetValueOrDefault("IsActive");
        return raw is null || string.IsNullOrWhiteSpace(raw) ? defaultValue : ParseBool(raw, defaultValue);
    }

    public static bool IsActive(Dictionary<string, string?> raw, List<string> headers, bool defaultValue)
    {
        var cell = ImportSheet.Value(raw, headers, ActiveHeaders);
        return cell is null ? defaultValue : ParseBool(cell, defaultValue);
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        var v = value.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ||
            v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("نعم", StringComparison.Ordinal))
            return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" ||
            v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("لا", StringComparison.Ordinal))
            return false;
        return defaultValue;
    }
}
