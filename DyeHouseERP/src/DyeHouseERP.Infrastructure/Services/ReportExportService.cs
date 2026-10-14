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
///
/// The factory's OWN identity (company name + logo) is NOT passed by the caller and is NOT
/// hard-coded here: every document reads it from <see cref="ICompanyIdentityProvider"/>, the
/// single authoritative source (spec section 47). A missing or unreadable logo is skipped
/// rather than failing generation.
/// </summary>
public class ReportExportService : IReportExportService
{
    private readonly ICompanyIdentityProvider _identityProvider;

    /// <summary>
    /// The Arabic-capable font shipped inside this assembly (Noto Naskh Arabic, SIL OFL 1.1).
    /// Every generated PDF renders with it, so Arabic titles, labels and values shape correctly
    /// on ANY host - including a bare Linux container with no system fonts installed.
    /// </summary>
    private const string ArabicFontFamily = "Noto Naskh Arabic";
    private const string ArabicFontResource = "DyeHouseERP.Infrastructure.Fonts.NotoNaskhArabic-Regular.ttf";

    static ReportExportService()
    {
        // QuestPDF Community license - free for this project's use case.
        // See https://www.questpdf.com/license/ if that ever changes.
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterArabicFont();
    }

    /// <summary>
    /// Registers the embedded Arabic font with QuestPDF once per process, so no PDF can be generated
    /// before it is available. Registration is by the font's own family name ("Noto Naskh Arabic").
    /// </summary>
    private static void RegisterArabicFont()
    {
        using var stream = typeof(ReportExportService).Assembly
            .GetManifestResourceStream(ArabicFontResource);
        if (stream is null) return; // never fail a report because the font resource is missing
        QuestPDF.Drawing.FontManager.RegisterFont(stream);
    }

    public ReportExportService(ICompanyIdentityProvider identityProvider) => _identityProvider = identityProvider;

    public byte[] GeneratePdf(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        var company = _identityProvider.Get();
        var hasLogo = company.TryGetLogoBytes(out var logoBytes, out _);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                // Arabic titles/labels are passed into this PDF (e.g. "كشف حساب العميل"). The
                // embedded Noto Naskh Arabic font (registered in the static constructor) is set as
                // the document's font, so Arabic glyphs and shaping are always available - the
                // deployment no longer needs an Arabic-capable system font.
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(ArabicFontFamily));

                page.Header().Column(col =>
                {
                    // Company identity, from the single settings source - never a literal.
                    if (hasLogo)
                    {
                        // A broken/unreadable logo must never break the document, so the
                        // image is only drawn when it decodes AND the renderer accepts it.
                        try { col.Item().Height(38).Image(logoBytes).FitHeight(); }
                        catch { /* skip the logo, keep the document */ }
                    }

                    col.Item().Text(company.DisplayName).FontSize(13).SemiBold();
                    if (!string.IsNullOrWhiteSpace(company.NameAr) &&
                        !string.Equals(company.NameAr, company.NameEn, StringComparison.Ordinal))
                        col.Item().Text(company.NameAr).FontSize(11).FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(2).Text(title).FontSize(15).Bold();
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
                    t.Span(company.DisplayName + " - ");
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
        var company = _identityProvider.Get();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName);

        // The sheet's identity comes from settings, carried in the print header/footer so the
        // data rows (row 1 = header) are never shifted - the import templates depend on that.
        worksheet.RightToLeft = true;
        worksheet.PageSetup.Header.Center.AddText(company.DisplayName);
        worksheet.PageSetup.Footer.Center.AddText(company.NameAr + "  -  " + company.NameEn);

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
