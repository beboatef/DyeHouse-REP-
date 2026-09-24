using ClosedXML.Excel;
using DyeHouseERP.Application.Common.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DyeHouseERP.Infrastructure.Services;

/// <summary>
/// One shared implementation backs every PDF/Excel "report" required
/// throughout the spec (customer statement, negative-stock override
/// report, etc. - spec sections 17, 33). Callers pass plain
/// headers + rows; this only handles rendering, so a new report is just a
/// new query + a call to one of these two methods.
/// </summary>
public class ReportExportService : IReportExportService
{
    static ReportExportService()
    {
        // QuestPDF Community license - free for this project's use case.
        // See https://www.questpdf.com/license/ if that ever changes.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                // NOTE: Arabic titles/labels are passed into this PDF (e.g. "كشف حساب
                // العميل"). Correct rendering needs a font with Arabic glyphs + shaping
                // actually available in the deployment environment - most Windows/macOS
                // dev machines have one by default, but a bare Linux container often
                // does NOT. If Arabic text renders as boxes/garbled on your server,
                // embed a font (e.g. Noto Naskh Arabic) via
                // QuestPDF.Drawing.FontManager.RegisterFont(stream) at startup and set
                // it here with .FontFamily("Noto Naskh Arabic").
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(title).FontSize(16).Bold();
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        col.Item().Text(subtitle).FontSize(10).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers) columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var h in headers)
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(h).Bold();
                    });

                    foreach (var row in rows)
                    {
                        foreach (var cell in row)
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(cell?.ToString() ?? "");
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("DyeHouse ERP - ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] GenerateExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName);

        for (var c = 0; c < headers.Count; c++)
        {
            var cell = worksheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var value = rows[r][c];
                var cell = worksheet.Cell(r + 2, c + 1);
                switch (value)
                {
                    case null: cell.Value = ""; break;
                    case decimal dec: cell.Value = dec; break;
                    case double dbl: cell.Value = dbl; break;
                    case int i: cell.Value = i; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    default: cell.Value = value.ToString(); break;
                }
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
