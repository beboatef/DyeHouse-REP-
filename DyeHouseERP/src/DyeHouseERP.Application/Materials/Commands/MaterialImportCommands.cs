using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Commands;

/// <summary>
/// Excel import for the Materials/Chemicals master (spec sections 24 + 38):
/// template -> upload -> preview/validate -> show errors -> confirm -> execute
/// -> result. Columns: Code, Name, Unit, PurchasePrice, Kind, ReorderLevel.
///
/// Unit must be exactly KG, Gram or Liter (no conversion between them). Kind
/// separates production chemicals from the operating-supplies store. Existing
/// codes are never overwritten automatically - they are only updated when the
/// caller holds items.edit (the API exposes that as import/preview-update and
/// import/execute-update).
/// </summary>
public record PreviewMaterialImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportPreviewDto>;

public class PreviewMaterialImportCommandHandler : IRequestHandler<PreviewMaterialImportCommand, ImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewMaterialImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<ImportPreviewDto> Handle(PreviewMaterialImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await MaterialImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        return new ImportPreviewDto
        {
            TotalRows = rows.Count,
            ValidRows = rows.Count(r => r.IsValid),
            InvalidRows = rows.Count(r => !r.IsValid),
            NewRows = rows.Count(r => !r.IsExisting),
            ExistingRows = rows.Count(r => r.IsExisting),
            UpdateExistingAllowed = request.AllowExistingUpdate,
            Rows = rows
        };
    }
}

public record ExecuteMaterialImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportExecuteResultDto>;

public class ExecuteMaterialImportCommandHandler : IRequestHandler<ExecuteMaterialImportCommand, ImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteMaterialImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    { _db = db; _reader = reader; _currentUser = currentUser; }

    public async Task<ImportExecuteResultDto> Handle(ExecuteMaterialImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await MaterialImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        var codes = rows.Select(r => r.Values.GetValueOrDefault("Code")).Where(c => c is not null).ToList();
        var existing = await _db.Materials.Where(m => codes.Contains(m.Code)).ToListAsync(cancellationToken);

        var created = 0;
        var updated = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            var code = row.Values.GetValueOrDefault("Code") ?? string.Empty;

            if (!row.IsValid)
            {
                errors.Add($"Row {row.RowNumber} ({code}): {string.Join("; ", row.Errors)}");
                continue;
            }

            var unit = Enum.Parse<MaterialUnit>(row.Values.GetValueOrDefault("Unit")!, ignoreCase: true);
            var kind = Enum.Parse<MaterialKind>(row.Values.GetValueOrDefault("Kind") ?? "Chemical", ignoreCase: true);
            var name = row.Values.GetValueOrDefault("Name")!;
            var price = decimal.Parse(row.Values.GetValueOrDefault("PurchasePrice") ?? "0");
            var reorder = row.Values.GetValueOrDefault("ReorderLevel") is { Length: > 0 } r ? decimal.Parse(r) : (decimal?)null;

            var match = existing.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var material = new Material(code, name, unit, price, _currentUser.UserName, kind);
                material.SetReorderLevel(reorder, _currentUser.UserName);
                _db.Materials.Add(material);
                row.Action = "Create";
                created++;
                continue;
            }

            match.SetName(name, _currentUser.UserName);
            match.SetKind(kind, _currentUser.UserName);
            match.SetPurchasePrice(price, _currentUser.UserName);
            match.SetReorderLevel(reorder, _currentUser.UserName);
            row.Action = "Update";
            if (match.Unit != unit)
                row.Errors.Add($"Unit was left unchanged ({match.Unit}); change it on the material itself so historical quantities are never reinterpreted.");
            updated++;
        }

        if (created > 0 || updated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new ImportExecuteResultDto
        {
            Created = created,
            Updated = updated,
            Skipped = rows.Count(r => !r.IsValid),
            Errors = errors,
            Rows = rows
        };
    }
}

internal static class MaterialImportValidator
{
    public static async Task<List<ImportRowPreview>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader,
        bool allowExistingUpdate, CancellationToken cancellationToken)
    {
        var rawRows = ImportSheet.Read(fileBytes, reader, out var headers);

        var existingCodes = (await db.Materials.AsNoTracking().Select(m => m.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowPreview>();
        var rowNumber = 1; // row 1 is the header row

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = ImportSheet.Value(raw, headers, "Code", "Material Code", "الكود");
            var name = ImportSheet.Value(raw, headers, "Name", "NameAr", "الاسم بالعربية", "الاسم");
            var unit = ImportSheet.Value(raw, headers, "Unit", "BaseUnit", "وحدة القياس");
            var price = ImportSheet.Value(raw, headers, "PurchasePrice", "Price", "سعر الشراء");
            var kind = ImportSheet.Value(raw, headers, "Kind", "النوع");
            var reorder = ImportSheet.Value(raw, headers, "ReorderLevel", "MinLevel", "حد الطلب");

            var row = new ImportRowPreview
            {
                RowNumber = rowNumber,
                Values = new Dictionary<string, string?>
                {
                    ["Code"] = code, ["Name"] = name, ["Unit"] = unit,
                    ["PurchasePrice"] = price, ["Kind"] = kind ?? "Chemical", ["ReorderLevel"] = reorder
                }
            };

            if (string.IsNullOrWhiteSpace(code)) row.Errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(name)) row.Errors.Add("Name is required.");

            if (string.IsNullOrWhiteSpace(unit)) row.Errors.Add("Unit is required (KG, Gram or Liter).");
            else if (!Enum.TryParse<MaterialUnit>(unit, ignoreCase: true, out _))
                row.Errors.Add($"Unit '{unit}' is not valid - it must be KG, Gram or Liter. Units are never converted into each other.");

            if (!string.IsNullOrWhiteSpace(kind) && !Enum.TryParse<MaterialKind>(kind, ignoreCase: true, out _))
                row.Errors.Add($"Kind '{kind}' is not valid - it must be Chemical or OperatingSupply.");

            if (!string.IsNullOrWhiteSpace(price) && (!decimal.TryParse(price, out var p) || p < 0))
                row.Errors.Add($"PurchasePrice '{price}' must be a number that is zero or greater.");

            if (!string.IsNullOrWhiteSpace(reorder) && (!decimal.TryParse(reorder, out var r) || r < 0))
                row.Errors.Add($"ReorderLevel '{reorder}' must be a number that is zero or greater.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (!seenInFile.Add(code))
                    row.Errors.Add($"Code '{code}' is duplicated within the file.");

                row.IsExisting = existingCodes.Contains(code);
                if (row.IsExisting)
                {
                    if (!allowExistingUpdate)
                        row.Errors.Add($"Code '{code}' already exists. Existing materials are not overwritten automatically.");
                    else
                        row.Action = "Update";
                }
            }

            row.IsValid = row.Errors.Count == 0;
            if (!row.IsValid) row.Action = "Skip";

            results.Add(row);
        }

        return results;
    }
}

/// <summary>Column headers of the material import template (spec section 38 step 1).</summary>
public static class MaterialImportTemplate
{
    public static readonly string[] Headers = { "Code", "Name", "Unit", "PurchasePrice", "Kind", "ReorderLevel" };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[] { "CHM-001", "صبغة حمراء", "KG", 145.5m, "Chemical", 50m },
        new object?[] { "SUP-001", "قفازات واقية", "Gram", 12m, "OperatingSupply", null }
    };
}
