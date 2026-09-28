using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Warehouses.Commands;

/// <summary>
/// Excel import for the Warehouse master (spec sections 15 + 38).
/// Columns: Code, Name, Kind (RawMaterial / Materials / ReadyGoods / ProductionWip / OperatingSupplies).
///
/// Existing warehouse codes are ALWAYS skipped here, even for a user with full
/// rights: changing a warehouse's Kind would silently reinterpret every
/// historical balance it holds (a material store becoming a ready-goods store).
/// Warehouses are therefore create-only through import; an existing one must be
/// corrected deliberately, on its own screen.
/// </summary>
public record PreviewWarehouseImportCommand(byte[] FileBytes) : IRequest<ImportPreviewDto>;

public class PreviewWarehouseImportCommandHandler : IRequestHandler<PreviewWarehouseImportCommand, ImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewWarehouseImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<ImportPreviewDto> Handle(PreviewWarehouseImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await WarehouseImportValidator.ValidateAsync(request.FileBytes, _db, _reader, cancellationToken);

        return new ImportPreviewDto
        {
            TotalRows = rows.Count,
            ValidRows = rows.Count(r => r.IsValid),
            InvalidRows = rows.Count(r => !r.IsValid),
            NewRows = rows.Count(r => !r.IsExisting),
            ExistingRows = rows.Count(r => r.IsExisting),
            UpdateExistingAllowed = false,
            Rows = rows
        };
    }
}

public record ExecuteWarehouseImportCommand(byte[] FileBytes) : IRequest<ImportExecuteResultDto>;

public class ExecuteWarehouseImportCommandHandler : IRequestHandler<ExecuteWarehouseImportCommand, ImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteWarehouseImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    { _db = db; _reader = reader; _currentUser = currentUser; }

    public async Task<ImportExecuteResultDto> Handle(ExecuteWarehouseImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await WarehouseImportValidator.ValidateAsync(request.FileBytes, _db, _reader, cancellationToken);

        var created = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            var code = row.Values.GetValueOrDefault("Code") ?? string.Empty;

            if (!row.IsValid)
            {
                errors.Add($"Row {row.RowNumber} ({code}): {string.Join("; ", row.Errors)}");
                continue;
            }

            var kind = Enum.Parse<WarehouseKind>(row.Values.GetValueOrDefault("Kind")!, ignoreCase: true);
            _db.Warehouses.Add(new Warehouse(code, row.Values.GetValueOrDefault("Name")!, kind, _currentUser.UserName));
            row.Action = "Create";
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new ImportExecuteResultDto
        {
            Created = created,
            Updated = 0,
            Skipped = rows.Count(r => !r.IsValid),
            Errors = errors,
            Rows = rows
        };
    }
}

internal static class WarehouseImportValidator
{
    public static async Task<List<ImportRowPreview>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader, CancellationToken cancellationToken)
    {
        var rawRows = ImportSheet.Read(fileBytes, reader, out var headers);

        var existingCodes = (await db.Warehouses.AsNoTracking().Select(w => w.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowPreview>();
        var rowNumber = 1;

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = ImportSheet.Value(raw, headers, "Code", "Warehouse Code", "كود المخزن");
            var name = ImportSheet.Value(raw, headers, "Name", "الاسم", "اسم المخزن");
            var kind = ImportSheet.Value(raw, headers, "Kind", "Type", "النوع");

            var row = new ImportRowPreview
            {
                RowNumber = rowNumber,
                Values = new Dictionary<string, string?> { ["Code"] = code, ["Name"] = name, ["Kind"] = kind }
            };

            if (string.IsNullOrWhiteSpace(code)) row.Errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(name)) row.Errors.Add("Name is required.");

            if (string.IsNullOrWhiteSpace(kind)) row.Errors.Add("Kind is required (RawMaterial, Materials, ReadyGoods, ProductionWip or OperatingSupplies).");
            else if (!Enum.TryParse<WarehouseKind>(kind, ignoreCase: true, out _))
                row.Errors.Add($"Kind '{kind}' is not valid. Customer-owned stock uses RawMaterial/ProductionWip/ReadyGoods; factory-owned stock uses Materials/OperatingSupplies.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (!seenInFile.Add(code))
                    row.Errors.Add($"Code '{code}' is duplicated within the file.");

                row.IsExisting = existingCodes.Contains(code);
                if (row.IsExisting)
                    row.Errors.Add($"Warehouse '{code}' already exists. Warehouses are never overwritten by import - its Kind decides how historic balances are interpreted.");
            }

            row.IsValid = row.Errors.Count == 0;
            if (!row.IsValid) row.Action = "Skip";

            results.Add(row);
        }

        return results;
    }
}

/// <summary>Column headers of the warehouse import template.</summary>
public static class WarehouseImportTemplate
{
    public static readonly string[] Headers = { "Code", "Name", "Kind" };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[] { "WH-RAW-2", "مخزن الخام ٢", "RawMaterial" },
        new object?[] { "WH-MAT-2", "مخزن المواد ٢", "Materials" }
    };
}
