using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Commands;

/// <summary>
/// Excel import for Customers, following the safe workflow spec section 38
/// requires: Upload -&gt; Read -&gt; Map -&gt; Validate -&gt; Preview -&gt; Show Errors -&gt;
/// Confirm -&gt; Execute -&gt; Results. This command is the "Preview" step - it
/// validates every row (duplicate codes, missing required fields) and
/// returns the results WITHOUT writing anything to the database.
/// Expected columns: Code, Name (header names case-insensitive).
/// </summary>
public record PreviewCustomerImportCommand(byte[] FileBytes) : IRequest<CustomerImportPreviewDto>;

public class PreviewCustomerImportCommandHandler : IRequestHandler<PreviewCustomerImportCommand, CustomerImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewCustomerImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<CustomerImportPreviewDto> Handle(PreviewCustomerImportCommand request, CancellationToken cancellationToken)
    {
        var results = await CustomerImportValidator.ValidateAsync(request.FileBytes, _db, _reader, cancellationToken);

        return new CustomerImportPreviewDto
        {
            Rows = results,
            ValidCount = results.Count(r => r.IsValid),
            InvalidCount = results.Count(r => !r.IsValid)
        };
    }
}

/// <summary>
/// The "Confirm -&gt; Execute -&gt; Results" step. Re-validates from scratch
/// (never trusts a stale client-side preview) and commits only the rows
/// that are still valid at commit time - so a row that became a duplicate
/// between preview and confirm (e.g. someone else imported it meanwhile)
/// is safely skipped, not silently double-created.
/// </summary>
public record ExecuteCustomerImportCommand(byte[] FileBytes) : IRequest<CustomerImportExecuteResultDto>;

public class ExecuteCustomerImportCommandHandler : IRequestHandler<ExecuteCustomerImportCommand, CustomerImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;
    public ExecuteCustomerImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    {
        _db = db; _reader = reader; _currentUser = currentUser;
    }

    public async Task<CustomerImportExecuteResultDto> Handle(ExecuteCustomerImportCommand request, CancellationToken cancellationToken)
    {
        var results = await CustomerImportValidator.ValidateAsync(request.FileBytes, _db, _reader, cancellationToken);

        var created = 0;
        foreach (var row in results.Where(r => r.IsValid))
        {
            var code = row.Values["Code"]!;
            var name = row.Values["Name"]!;
            _db.Customers.Add(new Customer(code, name, _currentUser.UserName));
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new CustomerImportExecuteResultDto
        {
            CreatedCount = created,
            SkippedInvalidCount = results.Count(r => !r.IsValid),
            Errors = results.Where(r => !r.IsValid).ToList()
        };
    }
}

/// <summary>Shared row-level validation so Preview and Execute can never disagree about what's valid.</summary>
internal static class CustomerImportValidator
{
    public static async Task<List<ImportRowResult>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(fileBytes);
        var rawRows = reader.ReadRows(stream, out var headers);

        var codeHeader = headers.FirstOrDefault(h => h.Equals("Code", StringComparison.OrdinalIgnoreCase)) ?? "Code";
        var nameHeader = headers.FirstOrDefault(h => h.Equals("Name", StringComparison.OrdinalIgnoreCase)) ?? "Name";

        var existingCodes = (await db.Customers.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowResult>();
        var rowNumber = 1; // row 1 is the header row, data starts at 2

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = raw.GetValueOrDefault(codeHeader)?.Trim();
            var name = raw.GetValueOrDefault(nameHeader)?.Trim();

            string? error = null;
            if (string.IsNullOrWhiteSpace(code)) error = "Code is required.";
            else if (string.IsNullOrWhiteSpace(name)) error = "Name is required.";
            else if (existingCodes.Contains(code)) error = $"Code '{code}' already exists.";
            else if (!seenInFile.Add(code)) error = $"Code '{code}' is duplicated within the file.";

            results.Add(new ImportRowResult
            {
                RowNumber = rowNumber,
                IsValid = error is null,
                Error = error,
                Values = new Dictionary<string, string?> { ["Code"] = code, ["Name"] = name }
            });
        }

        return results;
    }
}
