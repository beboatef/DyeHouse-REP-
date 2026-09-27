using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Commands;

/// <summary>
/// Excel import for the Item master, following the workflow spec section 7
/// requires: Download template -&gt; Upload -&gt; Preview -&gt; Validate -&gt; Show errors
/// -&gt; Confirm -&gt; Create items -&gt; Import result.
///
/// Expected columns (case-insensitive): Code, NameAr, NameEn, Category, BaseUnit.
///   - BaseUnit must be exactly KG or Meter (no conversion, no "Top/توب").
///   - Duplicate codes inside the file are rejected.
///   - Existing item codes are NEVER overwritten automatically: they are
///     reported as existing, and are only updated by the execute step when the
///     caller passed an explicit allowExistingUpdate flag (which the API only
///     exposes behind the items.edit permission - see UpdateExistingAllowed).
///
/// This command is the Preview step: it validates every row and writes nothing.
/// </summary>
public record PreviewItemImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ItemImportPreviewDto>;

public class PreviewItemImportCommandHandler : IRequestHandler<PreviewItemImportCommand, ItemImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewItemImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<ItemImportPreviewDto> Handle(PreviewItemImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await ItemImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        return new ItemImportPreviewDto
        {
            TotalRows = rows.Count,
            ValidRows = rows.Count(r => r.IsValid),
            InvalidRows = rows.Count(r => !r.IsValid),
            NewRows = rows.Count(r => !r.IsExisting),
            ExistingRows = rows.Count(r => r.IsExisting),
            UpdateExistingAllowed = request.AllowExistingUpdate,
            Rows = rows
        };
    }
}

/// <summary>
/// The Confirm -&gt; Create/Update -&gt; Result step. Re-validates from scratch (a
/// stale client-side preview is never trusted) and reports every row's outcome.
/// </summary>
public record ExecuteItemImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ItemImportExecuteResultDto>;

public class ExecuteItemImportCommandHandler : IRequestHandler<ExecuteItemImportCommand, ItemImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteItemImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    {
        _db = db; _reader = reader; _currentUser = currentUser;
    }

    public async Task<ItemImportExecuteResultDto> Handle(ExecuteItemImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await ItemImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        var existingItems = await _db.Items
            .Where(i => rows.Select(r => r.Code).Contains(i.Code))
            .ToListAsync(cancellationToken);

        var created = 0;
        var updated = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            if (!row.IsValid)
            {
                errors.Add($"Row {row.RowNumber} ({row.Code}): {string.Join("; ", row.Errors)}");
                continue;
            }

            var unit = Enum.Parse<UnitOfMeasure>(row.BaseUnit, ignoreCase: true);
            var existing = existingItems.FirstOrDefault(i => string.Equals(i.Code, row.Code, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _db.Items.Add(new Item(row.Code, row.Name, unit, _currentUser.UserName,
                    row.NameAr, row.NameEn, row.Category));
                row.Action = "Create";
                created++;
                continue;
            }

            if (!request.AllowExistingUpdate)
            {
                // Existing items are never silently overwritten (spec section 7).
                row.IsValid = false;
                row.Action = "Skip";
                row.Errors.Add("An item with this code already exists. Updating existing items requires the items.edit permission.");
                errors.Add($"Row {row.RowNumber} ({row.Code}): already exists and was not updated.");
                continue;
            }

            existing.SetNames(row.NameAr, row.NameEn);
            existing.SetCategory(row.Category);
            row.Action = "Update";
            if (existing.BaseUnit != unit)
                row.Errors.Add($"Base unit was left unchanged ({existing.BaseUnit}); change it on the item itself so historical quantities are never reinterpreted.");
            updated++;
        }

        if (created > 0 || updated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new ItemImportExecuteResultDto
        {
            Created = created,
            Updated = updated,
            Skipped = rows.Count(r => !r.IsValid),
            Errors = errors,
            Rows = rows
        };
    }
}

/// <summary>Shared row-level validation so Preview and Execute can never disagree about what is valid.</summary>
internal static class ItemImportValidator
{
    public static async Task<List<ItemImportRowResult>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader,
        bool allowExistingUpdate, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(fileBytes);
        var rawRows = reader.ReadRows(stream, out var headers);

        var existingCodes = (await db.Items.AsNoTracking().Select(i => i.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ItemImportRowResult>();
        var rowNumber = 1; // row 1 is the header row, data starts at 2

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = Header(raw, headers, "Code");
            var nameAr = Header(raw, headers, "NameAr", "Name Arabic", "الاسم بالعربية");
            var nameEn = Header(raw, headers, "NameEn", "Name English", "الاسم بالإنجليزية", "Name");
            var category = Header(raw, headers, "Category", "التصنيف");
            var baseUnit = Header(raw, headers, "BaseUnit", "Base Unit", "Unit", "وحدة القياس");

            var result = new ItemImportRowResult
            {
                RowNumber = rowNumber,
                Code = code ?? string.Empty,
                NameAr = nameAr ?? string.Empty,
                NameEn = nameEn ?? string.Empty,
                Name = nameEn ?? nameAr ?? string.Empty,
                Category = category,
                BaseUnit = baseUnit ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(code))
                result.Errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
                result.Errors.Add("At least one item name is required (NameAr or NameEn).");
            if (string.IsNullOrWhiteSpace(baseUnit))
                result.Errors.Add("BaseUnit is required (KG or Meter).");
            else if (!Enum.TryParse<UnitOfMeasure>(baseUnit, ignoreCase: true, out _))
                result.Errors.Add($"BaseUnit '{baseUnit}' is not valid - it must be KG or Meter. KG and Meter are never converted into each other.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (!seenInFile.Add(code))
                    result.Errors.Add($"Code '{code}' is duplicated within the file.");

                result.IsExisting = existingCodes.Contains(code);
                if (result.IsExisting)
                {
                    if (!allowExistingUpdate)
                        result.Errors.Add($"Code '{code}' already exists. Existing items are not overwritten automatically.");
                    else
                        result.Action = "Update";
                }
            }

            result.IsValid = result.Errors.Count == 0;
            if (!result.IsValid) result.Action = "Skip";

            results.Add(result);
        }

        return results;
    }

    private static string? Header(Dictionary<string, string?> row, List<string> headers, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var key = headers.FirstOrDefault(h => string.Equals(h, candidate, StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var value = row.GetValueOrDefault(key)?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}

/// <summary>Column headers of the item import template (spec section 7 step 1: "Download template").</summary>
public static class ItemImportTemplate
{
    public static readonly string[] Headers = { "Code", "NameAr", "NameEn", "Category", "BaseUnit" };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[] { "ITM-001", "قطن 100", "Cotton 100", "Fabric", "KG" },
        new object?[] { "ITM-002", "قماش سادة", "Plain Fabric", "Fabric", "Meter" }
    };
}
