using DyeHouseERP.Application.Common.Import;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>
/// Excel import for the Employee master (spec sections 36 + 38).
/// Columns: Code, NameAr, NameEn, Department, JobTitle, BasicSalary, HireDate, Phone, NationalId, BankAccount, Notes.
///
/// The Department column is matched against the department CODE first and then
/// against its Arabic/English name - a row naming a department that does not
/// exist is rejected with that exact reason instead of being silently created
/// or defaulted into the wrong department. BasicSalary and HireDate are
/// validated as real numbers/dates. Existing employee codes are skipped unless
/// the caller holds payroll.employees (the update endpoints).
/// </summary>
public record PreviewEmployeeImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportPreviewDto>;

public class PreviewEmployeeImportCommandHandler : IRequestHandler<PreviewEmployeeImportCommand, ImportPreviewDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    public PreviewEmployeeImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader) { _db = db; _reader = reader; }

    public async Task<ImportPreviewDto> Handle(PreviewEmployeeImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await EmployeeImportValidator.ValidateAsync(
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

public record ExecuteEmployeeImportCommand(byte[] FileBytes, bool AllowExistingUpdate = false) : IRequest<ImportExecuteResultDto>;

public class ExecuteEmployeeImportCommandHandler : IRequestHandler<ExecuteEmployeeImportCommand, ImportExecuteResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelImportReader _reader;
    private readonly ICurrentUserService _currentUser;

    public ExecuteEmployeeImportCommandHandler(IApplicationDbContext db, IExcelImportReader reader, ICurrentUserService currentUser)
    { _db = db; _reader = reader; _currentUser = currentUser; }

    public async Task<ImportExecuteResultDto> Handle(ExecuteEmployeeImportCommand request, CancellationToken cancellationToken)
    {
        var rows = await EmployeeImportValidator.ValidateAsync(
            request.FileBytes, _db, _reader, request.AllowExistingUpdate, cancellationToken);

        var departments = await _db.Departments.AsNoTracking().ToListAsync(cancellationToken);
        var codes = rows.Select(r => r.Values.GetValueOrDefault("Code")).Where(c => c is not null).ToList();
        var existing = await _db.Employees.Where(e => codes.Contains(e.Code)).ToListAsync(cancellationToken);

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

            var departmentKey = row.Values.GetValueOrDefault("Department") ?? string.Empty;
            var department = departments.FirstOrDefault(d =>
                string.Equals(d.Code, departmentKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.NameAr, departmentKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.NameEn, departmentKey, StringComparison.OrdinalIgnoreCase));

            if (department is null)
            {
                // Validation already guaranteed this cannot happen; kept as a guard
                // so a department deleted between preview and execute still cannot
                // create an employee with no department.
                errors.Add($"Row {row.RowNumber} ({code}): department '{departmentKey}' no longer exists.");
                row.IsValid = false;
                row.Action = "Skip";
                continue;
            }

            var nameAr = row.Values.GetValueOrDefault("NameAr");
            var nameEn = row.Values.GetValueOrDefault("NameEn");
            var salary = decimal.Parse(row.Values.GetValueOrDefault("BasicSalary") ?? "0");
            var hireDate = DateTime.Parse(row.Values.GetValueOrDefault("HireDate")!);
            var jobTitle = row.Values.GetValueOrDefault("JobTitle");
            var phone = row.Values.GetValueOrDefault("Phone");
            var nationalId = row.Values.GetValueOrDefault("NationalId");
            var bank = row.Values.GetValueOrDefault("BankAccount");
            var notes = row.Values.GetValueOrDefault("Notes");

            var match = existing.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _db.Employees.Add(new Employee(code, nameEn ?? nameAr!, department.Id, salary, hireDate,
                    _currentUser.UserName, nameAr, nameEn, jobTitle, phone, nationalId, bank, notes));
                row.Action = "Create";
                created++;
                continue;
            }

            match.SetNames(nameAr, nameEn);
            match.SetSalaryAndDepartment(department.Id, salary);
            match.SetJobTitle(jobTitle);
            match.SetHrDetails(phone, nationalId, bank, notes);
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

internal static class EmployeeImportValidator
{
    public static async Task<List<ImportRowPreview>> ValidateAsync(
        byte[] fileBytes, IApplicationDbContext db, IExcelImportReader reader,
        bool allowExistingUpdate, CancellationToken cancellationToken)
    {
        var rawRows = ImportSheet.Read(fileBytes, reader, out var headers);

        var departments = await db.Departments.AsNoTracking()
            .Select(d => new { d.Code, d.NameAr, d.NameEn })
            .ToListAsync(cancellationToken);

        var existingCodes = (await db.Employees.AsNoTracking().Select(e => e.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowPreview>();
        var rowNumber = 1;

        foreach (var raw in rawRows)
        {
            rowNumber++;
            var code = ImportSheet.Value(raw, headers, "Code", "Employee Code", "كود الموظف");
            var nameAr = ImportSheet.Value(raw, headers, "NameAr", "Name Arabic", "الاسم بالعربية", "الاسم");
            var nameEn = ImportSheet.Value(raw, headers, "NameEn", "Name English", "Name", "الاسم بالإنجليزية");
            var department = ImportSheet.Value(raw, headers, "Department", "DepartmentCode", "القسم", "كود القسم");
            var jobTitle = ImportSheet.Value(raw, headers, "JobTitle", "Title", "الوظيفة");
            var salary = ImportSheet.Value(raw, headers, "BasicSalary", "Salary", "الراتب الأساسي");
            var hireDate = ImportSheet.Value(raw, headers, "HireDate", "تاريخ التعيين");
            var phone = ImportSheet.Value(raw, headers, "Phone", "الهاتف");
            var nationalId = ImportSheet.Value(raw, headers, "NationalId", "الرقم القومي");
            var bank = ImportSheet.Value(raw, headers, "BankAccount", "BankAccountNumber", "الحساب البنكي");
            var notes = ImportSheet.Value(raw, headers, "Notes", "ملاحظات");

            var row = new ImportRowPreview
            {
                RowNumber = rowNumber,
                Values = new Dictionary<string, string?>
                {
                    ["Code"] = code, ["NameAr"] = nameAr, ["NameEn"] = nameEn, ["Department"] = department,
                    ["JobTitle"] = jobTitle, ["BasicSalary"] = salary, ["HireDate"] = hireDate,
                    ["Phone"] = phone, ["NationalId"] = nationalId, ["BankAccount"] = bank, ["Notes"] = notes
                }
            };

            if (string.IsNullOrWhiteSpace(code)) row.Errors.Add("Code is required.");
            if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
                row.Errors.Add("At least one employee name is required (NameAr or NameEn).");

            if (string.IsNullOrWhiteSpace(department))
            {
                row.Errors.Add("Department is required (use the department code or name).");
            }
            else if (!departments.Any(d =>
                string.Equals(d.Code, department, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.NameAr, department, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.NameEn, department, StringComparison.OrdinalIgnoreCase)))
            {
                row.Errors.Add($"Department '{department}' does not exist. Create it first - employees are never filed under a missing department.");
            }

            if (string.IsNullOrWhiteSpace(salary)) row.Errors.Add("BasicSalary is required.");
            else if (!decimal.TryParse(salary, out var s) || s < 0)
                row.Errors.Add($"BasicSalary '{salary}' must be a number that is zero or greater.");

            if (string.IsNullOrWhiteSpace(hireDate)) row.Errors.Add("HireDate is required.");
            else if (!DateTime.TryParse(hireDate, out _))
                row.Errors.Add($"HireDate '{hireDate}' is not a valid date.");

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (!seenInFile.Add(code))
                    row.Errors.Add($"Code '{code}' is duplicated within the file.");

                row.IsExisting = existingCodes.Contains(code);
                if (row.IsExisting)
                {
                    if (!allowExistingUpdate)
                        row.Errors.Add($"Code '{code}' already exists. Existing employees are not overwritten automatically.");
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

/// <summary>Column headers of the employee import template.</summary>
public static class EmployeeImportTemplate
{
    public static readonly string[] Headers =
    {
        "Code", "NameAr", "NameEn", "Department", "JobTitle", "BasicSalary", "HireDate",
        "Phone", "NationalId", "BankAccount", "Notes"
    };

    public static List<object?[]> SampleRows() => new()
    {
        new object?[]
        {
            "EMP-001", "محمد علي", "Mohamed Ali", "DYE", "عامل صباغة", 6500m, "2025-01-15",
            "01000000000", "29001010100000", "1234567890", ""
        }
    };
}
