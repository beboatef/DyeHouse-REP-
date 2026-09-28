using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Suppliers.Commands;

/// <summary>
/// Excel import for the Supplier master (spec sections 35 + 38).
/// Columns: Code, NameAr, NameEn, Phone, Address, ContactPerson, TaxNumber.
///
/// Phone/address stay optional (never required to save a supplier). Existing
/// codes are reported and skipped unless the caller holds suppliers.edit, which
/// is exactly the permission the API requires for import/preview-update and
/// import/execute-update - so a plain supplier.create user can never overwrite
/// an existing account.
/// </summary>
public record PreviewSupplierImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportPreviewDto>;

public class PreviewSupplierImportCommandHandler : IRequestHandler<PreviewSupplierImportCommand, ImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewSupplierImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<ImportPreviewDto> Handle(PreviewSupplierImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await SupplierImportValidator.ValidateAsync(
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

public record ExecuteSupplierImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportExecuteResultDto>;

public class ExecuteSupplierImportCommandHandler : IRequestHandler<ExecuteSupplierImportCommand, ImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteSupplierImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    { _db = db; _reader = reader; _currentUser = currentUser; }

    public async Task<ImportExecuteResultDto> Handle(ExecuteSupplierImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await SupplierImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        var codes = rows.Select(r => r.Values.GetValueOrDefault("Code")).Where(c => c is not null).ToList();
        var existing = await _db.Suppliers.Where(s => codes.Contains(s.Code)).ToListAsync(cancellationToken);

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

            var nameAr = row.Values.GetValueOrDefault("NameAr");
            var nameEn = row.Values.GetValueOrDefault("NameEn");
            var phone = row.Values.GetValueOrDefault("Phone");
            var address = row.Values.GetValueOrDefault("Address");
            var contact = row.Values.GetValueOrDefault("ContactPerson");
            var tax = row.Values.GetValueOrDefault("TaxNumber");

            var match = existing.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _db.Suppliers.Add(new Supplier(code, nameEn ?? nameAr!, _currentUser.UserName,
                    nameAr, nameEn, phone, address, contact, tax));
                row.Action = "Create";
                created++;
                continue;
            }

            match.SetNames(nameAr, nameEn);
            match.SetContact(phone, address, contact, tax);
            match.SetAccountNumber(match.Code);
            row.Action = "Update";
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

internal static class SupplierImportValidator
{
    public static async Task<List<ImportRowPreview>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader,
        bool allowExistingUpdate, CancellationToken cancellationToken)
    {
        var rawRows = ImportSheet.Read(fileBytes, reader, out var headers);

        var existingCodes = (await db.Suppliers.AsNoTracking().Select(s => s.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowPreview>();
        var rowNumber = 1;

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = ImportSheet.Value(raw, headers, "Code", "Supplier Code", "كود المورد");
            var nameAr = ImportSheet.Value(raw, headers, "NameAr", "Name Arabic", "الاسم بالعربية", "الاسم");
            var nameEn = ImportSheet.Value(raw, headers, "NameEn", "Name English", "Name", "الاسم بالإنجليزية");
            var phone = ImportSheet.Value(raw, headers, "Phone", "PhoneNumber", "الهاتف", "التليفون");
            var address = ImportSheet.Value(raw, headers, "Address", "العنوان");
            var contact = ImportSheet.Value(raw, headers, "ContactPerson", "Contact", "مسؤول التواصل");
            var tax = ImportSheet.Value(raw, headers, "TaxNumber", "TaxId", "الرقم الضريبي");

            var row = new ImportRowPreview
            {
                RowNumber = rowNumber,
                Values = new Dictionary<string, string?>
                {
                    ["Code"] = code, ["NameAr"] = nameAr, ["NameEn"] = nameEn,
                    ["Phone"] = phone, ["Address"] = address, ["ContactPerson"] = contact, ["TaxNumber"] = tax
                }
            };

            if (string.IsNullOrWhiteSpace(code)) row.Errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
                row.Errors.Add("At least one supplier name is required (NameAr or NameEn).");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (!seenInFile.Add(code))
                    row.Errors.Add($"Code '{code}' is duplicated within the file.");

                row.IsExisting = existingCodes.Contains(code);
                if (row.IsExisting)
                {
                    if (!allowExistingUpdate)
                        row.Errors.Add($"Code '{code}' already exists. Existing suppliers are not overwritten automatically.");
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

/// <summary>Column headers of the supplier import template.</summary>
public static class SupplierImportTemplate
{
    public static readonly string[] Headers = { "Code", "NameAr", "NameEn", "Phone", "Address", "ContactPerson", "TaxNumber" };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[] { "SUP-001", "شركة الأصباغ الحديثة", "Modern Dyes Co.", "01000000000", "القاهرة", "أحمد", "100-200-300" }
    };
}
