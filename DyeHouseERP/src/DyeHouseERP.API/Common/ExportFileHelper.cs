using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DyeHouseERP.API.Common;

/// <summary>
/// One place that turns headers + rows into a downloadable file, so every
/// export endpoint in the API behaves identically: `format=excel` returns an
/// .xlsx, `format=pdf` returns a printable A4 document, and the file name is
/// derived from a stable stem. Before this existed each controller carried its
/// own private copy of the same twelve lines.
/// </summary>
public static class ExportFileHelper
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IActionResult ToFile(IReportExportService export, string format, string title, string subtitle,
        IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, string fileStem)
        => ToFile(export, format, title, subtitle, headers, rows, fileStem, null);

    public static IActionResult ToFile(IReportExportService export, string format, string title, string subtitle,
        IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, string fileStem, string? excelSheetName)
    {
        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return new FileContentResult(export.GeneratePdf(title, subtitle, headers, rows), "application/pdf")
            { FileDownloadName = $"{fileStem}.pdf" };

        return new FileContentResult(export.GenerateExcel(excelSheetName ?? title, headers, rows), ExcelContentType)
        { FileDownloadName = $"{fileStem}.xlsx" };
    }

    /// <summary>Renders the same rows as a single-document PDF (a voucher/invoice, not a list).</summary>
    public static IActionResult ToPdf(IReportExportService export, string title, string subtitle,
        IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, string fileStem)
        => new FileContentResult(export.GeneratePdf(title, subtitle, headers, rows), "application/pdf")
        { FileDownloadName = $"{fileStem}.pdf" };

    /// <summary>The empty import template with its exact expected columns (spec section 38 step 1).</summary>
    public static IActionResult Template(IReportExportService export, string sheetName, string[] headers,
        List<object?[]> sampleRows, string fileStem)
        => new FileContentResult(export.GenerateExcel(sheetName, headers, sampleRows), ExcelContentType)
        { FileDownloadName = $"{fileStem}.xlsx" };

    public static async Task<byte[]> ReadUploadAsync(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return stream.ToArray();
    }
}
