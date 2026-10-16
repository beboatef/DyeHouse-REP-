using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Records the exact title, subtitle, headers and rows a controller asked the renderer for - and how
/// many PDFs were produced - without producing any bytes itself.
///
/// Tests use it for two different kinds of evidence: the DOCUMENT the application built (which headers,
/// which figures, which identity on each row), and the fact that a REFUSED request produced no document
/// at all (<see cref="PdfCalls"/> stays 0), which is what "the wrong request never yields a file" means
/// in practice.
/// </summary>
internal sealed class CapturingReportExportService : IReportExportService
{
    public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<object?[]> Rows { get; private set; } = Array.Empty<object?[]>();
    public string? Title { get; private set; }
    public string? Subtitle { get; private set; }

    /// <summary>How many PDFs were asked for - zero is the evidence that a refusal produced no file.</summary>
    public int PdfCalls { get; private set; }

    public byte[] GeneratePdf(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        PdfCalls++;
        Title = title;
        Subtitle = subtitle;
        Headers = headers.ToList();
        Rows = rows.Select(r => r.ToArray()).ToList();
        return new byte[] { 1 };
    }

    public byte[] GenerateExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
        => Array.Empty<byte>();
}
