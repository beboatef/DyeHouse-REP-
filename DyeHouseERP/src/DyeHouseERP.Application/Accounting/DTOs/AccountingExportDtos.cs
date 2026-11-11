namespace DyeHouseERP.Application.Accounting.DTOs;

/// <summary>
/// One worksheet of a multi-sheet XLSX export. Sheets are plain header/row
/// tables so the CSV export (one set per file) and the XLSX export (one set
/// per sheet) are built from the same rows by the same code.
/// </summary>
public sealed record ExportWorksheet(string SheetName, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

/// <summary>
/// Export set keys accepted by the accounting export. "all" is only valid for
/// XLSX (every set becomes a sheet); CSV carries exactly one set per file.
/// </summary>
public static class AccountingExportSets
{
    public const string CustomerInvoices = "customer-invoices";
    public const string Supplier = "supplier";
    public const string Treasury = "treasury";
    public const string Payroll = "payroll";
    public const string Cheques = "cheques";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CustomerInvoices, Supplier, Treasury, Payroll, Cheques
    };
}

/// <summary>
/// A generated accounting export file: the bytes plus the audit-relevant
/// metadata. Totals are appended to every table from the same rows, so the
/// totals row always equals the sum of the exported rows.
/// </summary>
public class AccountingExportFile
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public List<string> SetsIncluded { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string TimeZone { get; set; } = "UTC";
    /// <summary>Data rows across all sets (header and totals rows excluded).</summary>
    public int TotalRows { get; set; }
    public Dictionary<string, int> RowsPerSet { get; set; } = new();
}
