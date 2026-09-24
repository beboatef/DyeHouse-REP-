using ClosedXML.Excel;
using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.Infrastructure.Services;

/// <summary>Generic Excel row reader backing every "Excel Import" workflow (spec section 38) - one implementation, every import command supplies its own column mapping/validation on top.</summary>
public class ExcelReaderService : IExcelReaderService
{
    public List<Dictionary<string, string>> ReadRows(byte[] fileBytes, string sheetName = "")
    {
        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = string.IsNullOrWhiteSpace(sheetName) ? workbook.Worksheet(1) : workbook.Worksheet(sheetName);

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return new List<Dictionary<string, string>>();

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count < 2) return new List<Dictionary<string, string>>(); // header only or empty

        var headerRow = rows[0];
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();

        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(1))
        {
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = row.Cell(i + 1);
                dict[headers[i]] = cell.GetString().Trim();
            }
            // Skip fully blank rows (trailing empty rows Excel sometimes leaves behind).
            if (dict.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                result.Add(dict);
        }

        return result;
    }
}
