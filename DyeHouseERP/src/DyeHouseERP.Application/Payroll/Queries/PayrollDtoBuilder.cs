using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Queries;

/// <summary>
/// Maps payroll aggregates to DTOs. Derived amounts are computed here after
/// loading rather than inside a LINQ-to-Entities projection, because they are
/// unmapped domain members that EF cannot translate.
/// </summary>
internal static class PayrollDtoBuilder
{
    public static async Task<Dictionary<Guid, (string Code, string Name)>> DepartmentLookupAsync(
        IApplicationDbContext db, IEnumerable<Guid> departmentIds, CancellationToken cancellationToken)
    {
        var ids = departmentIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, (string, string)>();

        return await db.Departments.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.Code, d.Name })
            .ToDictionaryAsync(d => d.Id, d => (d.Code, d.Name), cancellationToken);
    }

    public static async Task<Dictionary<Guid, string>> TreasuryLookupAsync(
        IApplicationDbContext db, IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await db.TreasuryAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
    }

    public static EmployeeDto MapEmployee(Employee e, IReadOnlyDictionary<Guid, (string Code, string Name)> departments)
    {
        var department = departments.TryGetValue(e.DepartmentId, out var d) ? d : (Code: "", Name: "");

        return new EmployeeDto
        {
            Id = e.Id,
            Code = e.Code,
            Name = e.Name,
            NameAr = e.NameAr,
            NameEn = e.NameEn,
            DepartmentId = e.DepartmentId,
            DepartmentCode = department.Code,
            DepartmentName = department.Name,
            JobTitle = e.JobTitle,
            BasicSalary = e.BasicSalary,
            Status = e.Status,
            HireDate = e.HireDate,
            TerminationDate = e.TerminationDate,
            Phone = e.Phone,
            NationalId = e.NationalId,
            BankAccountNumber = e.BankAccountNumber,
            Notes = e.Notes
        };
    }

    public static PayrollRunDto MapRun(PayrollRun r, IReadOnlyDictionary<Guid, string> accounts)
    {
        var lines = r.Lines
            .OrderBy(l => l.EmployeeCode)
            .Select(l => new PayrollRunLineDto
            {
                Id = l.Id,
                EmployeeId = l.EmployeeId,
                EmployeeCode = l.EmployeeCode,
                EmployeeName = l.EmployeeName,
                DepartmentId = l.DepartmentId,
                DepartmentName = l.DepartmentName,
                BasicSalary = l.BasicSalary,
                Allowances = l.Allowances,
                Deductions = l.Deductions,
                GrossPay = l.GrossPay,
                NetPay = l.NetPay,
                Notes = l.Notes
            }).ToList();

        return new PayrollRunDto
        {
            Id = r.Id,
            RunNumber = r.RunNumber,
            PeriodYear = r.PeriodYear,
            PeriodMonth = r.PeriodMonth,
            TreasuryAccountId = r.TreasuryAccountId,
            TreasuryAccountName = r.TreasuryAccountId.HasValue
                ? accounts.GetValueOrDefault(r.TreasuryAccountId.Value)
                : null,
            Notes = r.Notes,
            Status = r.Status,
            TotalGross = r.TotalGross,
            TotalDeductions = r.TotalDeductions,
            TotalNet = r.TotalNet,
            EmployeeCount = lines.Count,
            ApprovedBy = r.ApprovedBy,
            ApprovedAtUtc = r.ApprovedAtUtc,
            PostedBy = r.PostedBy,
            PostedAtUtc = r.PostedAtUtc,
            CancellationReason = r.CancellationReason,
            CreatedBy = r.CreatedBy,
            CreatedAtUtc = r.CreatedAtUtc,
            Lines = lines
        };
    }
}
