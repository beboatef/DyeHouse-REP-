using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Commands;

/// <summary>Excel import for Items (spec section 38). Expected columns: Code, Name, BaseUnit (KG or Meter).</summary>
public record PreviewItemImportCommand(byte[] FileBytes) : IRequest<ItemImportPreviewDto>;

public class PreviewItemImportCommandHandler : IRequestHandler<PreviewItemImportCommand, ItemImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelReaderService _excelReader;
    public PreviewItemImportCommandHandler(IApplicationDbContext db, IExcelReaderService excelReader) { _db = db; _excelReader = excelReader; }

    public async Task<ItemImportPreviewDto> Handle(PreviewItemImportCommand request, CancellationToken cancellationToken)
    {
        var rows = _excelReader.ReadRows(request.FileBytes);
        var existingCodes = (await _db.Items.AsNoTracking().Select(i => i.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = ItemImportValidator.Validate(rows, existingCodes);

        return new ItemImportPreviewDto
        {
            TotalRows = results.Count, ValidRows = results.Count(r => r.IsValid),
            InvalidRows = results.Count(r => !r.IsValid), Rows = results
        };
    }
}

public record ExecuteItemImportCommand(byte[] FileBytes) : IRequest<ItemImportExecuteResultDto>;

public class ExecuteItemImportCommandHandler : IRequestHandler<ExecuteItemImportCommand, ItemImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelReaderService _excelReader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteItemImportCommandHandler(IApplicationDbContext db, IExcelReaderService excelReader, ICurrentUserService currentUser)
    {
        _db = db; _excelReader = excelReader; _currentUser = currentUser;
    }

    public async Task<ItemImportExecuteResultDto> Handle(ExecuteItemImportCommand request, CancellationToken cancellationToken)
    {
        var rows = _excelReader.ReadRows(request.FileBytes);
        var existingCodes = (await _db.Items.AsNoTracking().Select(i => i.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = ItemImportValidator.Validate(rows, existingCodes);

        var imported = 0;
        var errors = new List<string>();

        foreach (var row in results.Where(r => r.IsValid))
        {
            var unit = Enum.Parse<UnitOfMeasure>(row.BaseUnit, ignoreCase: true);
            _db.Items.Add(new Item(row.Code, row.Name, unit, _currentUser.UserName));
            imported++;
        }
        foreach (var row in results.Where(r => !r.IsValid))
            errors.Add($"Row {row.RowNumber}: {string.Join("; ", row.Errors)}");

        if (imported > 0) await _db.SaveChangesAsync(cancellationToken);

        return new ItemImportExecuteResultDto { Imported = imported, Skipped = results.Count - imported, Errors = errors };
    }
}

internal static class ItemImportValidator
{
    public static List<ItemImportRowResult> Validate(List<Dictionary<string, string>> rows, HashSet<string> existingCodes)
    {
        var results = new List<ItemImportRowResult>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;

        foreach (var row in rows)
        {
            rowNumber++;
            var code = GetValue(row, "Code");
            var name = GetValue(row, "Name");
            var baseUnit = GetValue(row, "BaseUnit");
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(code)) errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(name)) errors.Add("Name is required.");
            if (string.IsNullOrWhiteSpace(baseUnit)) errors.Add("BaseUnit is required (KG or Meter).");
            else if (!Enum.TryParse<UnitOfMeasure>(baseUnit, ignoreCase: true, out _)) errors.Add($"BaseUnit '{baseUnit}' must be KG or Meter.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (existingCodes.Contains(code)) errors.Add($"Code '{code}' already exists.");
                if (!seenInFile.Add(code)) errors.Add($"Code '{code}' is duplicated within the file.");
            }

            results.Add(new ItemImportRowResult { RowNumber = rowNumber, Code = code, Name = name, BaseUnit = baseUnit, IsValid = errors.Count == 0, Errors = errors });
        }

        return results;
    }

    private static string GetValue(Dictionary<string, string> row, string key)
    {
        var match = row.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        return match is null ? string.Empty : row[match];
    }
}
