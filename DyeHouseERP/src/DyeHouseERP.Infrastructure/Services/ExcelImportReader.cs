using ClosedXML.Excel;
using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.Infrastructure.Services;

/// <summary>
/// Reads an uploaded .xlsx file's first worksheet into plain
/// header-name -> cell-value dictionaries (spec section 38: Upload -> Read
/// -> Map -> Validate -> Preview -> ... ). Row 1 is treated as headers.
/// Deliberately dumb/generic - all entity-specific mapping and validation
/// happens in the Application command that consumes the rows, not here.
/// </summary>
public class ExcelImportReader : IExcelImportReader
{
    public List<Dictionary<string, string?>> ReadRows(Stream fileStream, out List<string> headers)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.First();

        var headerRow = worksheet.Row(1);
        headers = headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();

        var rows = new List<Dictionary<string, string?>>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var row = worksheet.Row(r);
            if (row.IsEmpty()) continue;

            var dict = new Dictionary<string, string?>();
            for (var c = 0; c < headers.Count; c++)
            {
                var cell = row.Cell(c + 1);
                dict[headers[c]] = cell.IsEmpty() ? null : cell.GetString().Trim();
            }
            rows.Add(dict);
        }

        return rows;
    }
}
