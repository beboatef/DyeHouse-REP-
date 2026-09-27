using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Payroll.DTOs;

/// <summary>
/// Payroll module DTOs (spec section 36). Amounts derived on the aggregates
/// (gross, net, run totals) are computed in memory after loading, never projected
/// inside a LINQ-to-Entities query, because they are unmapped domain members.
/// </summary>
public class DepartmentDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
}

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public decimal BasicSalary { get; set; }
    public EmployeeStatus Status { get; set; }
    public DateTime HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public string? Phone { get; set; }
    public string? NationalId { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? Notes { get; set; }
}

public class PayrollRunLineDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal Deductions { get; set; }
    public decimal GrossPay { get; set; }
    public decimal NetPay { get; set; }
    public string? Notes { get; set; }
}

public class PayrollRunDto
{
    public Guid Id { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public Guid? TreasuryAccountId { get; set; }
    public string? TreasuryAccountName { get; set; }
    public string? Notes { get; set; }
    public PayrollRunStatus Status { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public int EmployeeCount { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<PayrollRunLineDto> Lines { get; set; } = new();
}
