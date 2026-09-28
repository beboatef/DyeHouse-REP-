using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.Application.Common.Import;

/// <summary>
/// Shared shapes for every Excel import in the system (spec section 38), so the
/// UI can render one import panel and every module speaks the same language.
///
/// The contract that makes "no invalid row silently enters the database" true:
///   - Preview NEVER writes; it only classifies rows (Create / Update / Skip)
///     and lists the reason for every rejection.
///   - Execute re-validates the same bytes from scratch through the same
///     validator, so a stale preview cannot be trusted into the database, and
///     only rows valid at commit time are written.
///   - Rows that reference existing records are reported as existing and are
///     refused unless the caller holds the module's *edit* permission.
/// </summary>
public class ImportRowPreview
{
    public int RowNumber { get; set; }

    /// <summary>Canonical field name -> raw cell value, so the UI can show the row as it was read.</summary>
    public Dictionary<string, string?> Values { get; set; } = new();

    public bool IsValid { get; set; }

    /// <summary>True when the row's key already exists in the database.</summary>
    public bool IsExisting { get; set; }

    /// <summary>Create / Update / Skip - what Execute will do with this row.</summary>
    public string Action { get; set; } = "Create";

    public List<string> Errors { get; set; } = new();
}

public class ImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int NewRows { get; set; }
    public int ExistingRows { get; set; }

    /// <summary>False when existing keys were found but the caller may not update them.</summary>
    public bool UpdateExistingAllowed { get; set; }

    public List<ImportRowPreview> Rows { get; set; } = new();
}

public class ImportExecuteResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }

    /// <summary>One human-readable line per skipped/failed row - the import result report.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Full per-row outcome.</summary>
    public List<ImportRowPreview> Rows { get; set; } = new();
}

/// <summary>Reading helpers shared by every import validator.</summary>
public static class ImportSheet
{
    /// <summary>Reads the uploaded workbook into raw rows. Row 1 is the header row.</summary>
    public static List<Dictionary<string, string?>> Read(byte[] fileBytes, IExcelImportReader reader, out List<string> headers)
    {
        using var stream = new MemoryStream(fileBytes);
        return reader.ReadRows(stream, out headers);
    }

    /// <summary>
    /// Case-insensitive lookup that also accepts Arabic header names, so the
    /// same template works for an Arabic-speaking data-entry clerk and for an
    /// English-language export from another system (spec section 3).
    /// </summary>
    public static string? Value(Dictionary<string, string?> row, List<string> headers, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var key = headers.FirstOrDefault(h => string.Equals(h?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var value = row.GetValueOrDefault(key)?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>Trimmed cell value by header alias, or null.</summary>
    public static string? Raw(Dictionary<string, string?> row, List<string> headers, params string[] candidates)
        => Value(row, headers, candidates);

    public static string[] ParseError(string message) => new[] { message };
}
