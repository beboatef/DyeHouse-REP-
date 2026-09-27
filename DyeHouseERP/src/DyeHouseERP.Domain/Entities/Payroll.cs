using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Department master (spec section 36): Administration, HR, Accounting,
/// Warehouses, Production, Dyeing, Finishing, Lab/Quality, Maintenance,
/// Winding/Packing, Sales/Customer Service - and any others the factory adds.
/// Configurable data, never a hard-coded list.
/// </summary>
public class Department : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Department() { } // EF Core

    public Department(string code, string name, string createdBy,
        string? nameAr = null, string? nameEn = null, string? notes = null)
    {
        SetCode(code);
        SetNames(name, nameAr, nameEn);
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Department code is required.", nameof(code));
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn) => SetNames(null, nameAr, nameEn);

    public void SetNames(string? name, string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("Department name is required.", nameof(nameEn));

        NameEn = (nameEn ?? name ?? nameAr)!.Trim();
        NameAr = (nameAr ?? name ?? nameEn)!.Trim();
        Name = NameEn.Length > 0 ? NameEn : NameAr;
    }

    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public void Activate(string modifiedBy) { IsActive = true; Touch(modifiedBy); }
    public void Deactivate(string modifiedBy) { IsActive = false; Touch(modifiedBy); }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Employee master data (spec section 36): code, bilingual name, department, job
/// title, basic salary and status. Payroll is deliberately INDEPENDENT of Job
/// Order costing - nothing here is allocated to a job order automatically.
/// </summary>
public class Employee : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public Guid DepartmentId { get; private set; }
    public string? JobTitle { get; private set; }
    public decimal BasicSalary { get; private set; }
    public EmployeeStatus Status { get; private set; } = EmployeeStatus.Active;
    public DateTime HireDate { get; private set; }
    public DateTime? TerminationDate { get; private set; }
    public string? Phone { get; private set; }
    public string? NationalId { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? Notes { get; private set; }

    private Employee() { } // EF Core

    public Employee(string code, string name, Guid departmentId, decimal basicSalary, DateTime hireDate,
        string createdBy, string? nameAr = null, string? nameEn = null, string? jobTitle = null,
        string? phone = null, string? nationalId = null, string? bankAccountNumber = null, string? notes = null)
    {
        SetCode(code);
        SetNames(name, nameAr, nameEn);
        SetSalaryAndDepartment(departmentId, basicSalary);
        HireDate = hireDate;
        JobTitle = Blank(jobTitle);
        Phone = Blank(phone);
        NationalId = Blank(nationalId);
        BankAccountNumber = Blank(bankAccountNumber);
        Notes = Blank(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsPayable => Status == EmployeeStatus.Active;

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Employee code is required.", nameof(code));
        Code = code.Trim();
    }

    public void SetNames(string? nameAr, string? nameEn) => SetNames(null, nameAr, nameEn);

    public void SetNames(string? name, string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("Employee name is required.", nameof(nameEn));

        NameEn = (nameEn ?? name ?? nameAr)!.Trim();
        NameAr = (nameAr ?? name ?? nameEn)!.Trim();
        Name = NameEn.Length > 0 ? NameEn : NameAr;
    }

    public void SetSalaryAndDepartment(Guid departmentId, decimal basicSalary)
    {
        if (departmentId == Guid.Empty) throw new DomainException("An employee must belong to a department.");
        if (basicSalary < 0) throw new DomainException("A basic salary cannot be negative.");

        DepartmentId = departmentId;
        BasicSalary = basicSalary;
    }

    public void SetJobTitle(string? jobTitle) => JobTitle = Blank(jobTitle);

    public void SetHrDetails(string? phone, string? nationalId, string? bankAccountNumber, string? notes)
    {
        Phone = Blank(phone);
        NationalId = Blank(nationalId);
        BankAccountNumber = Blank(bankAccountNumber);
        Notes = Blank(notes);
    }

    public void Suspend(string modifiedBy)
    {
        Status = EmployeeStatus.Suspended;
        Touch(modifiedBy);
    }

    public void Reactivate(string modifiedBy)
    {
        Status = EmployeeStatus.Active;
        TerminationDate = null;
        Touch(modifiedBy);
    }

    public void Terminate(DateTime terminationDate, string modifiedBy)
    {
        Status = EmployeeStatus.Terminated;
        TerminationDate = terminationDate;
        Touch(modifiedBy);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Monthly payroll run (spec section 36). One run per period, built from the active
/// employees' basic salaries plus allowances and deductions. Posting is the ONLY
/// step with a financial effect: at that point one OUT TreasuryTransaction is
/// written for the net total. Cancelling posts the equal-and-opposite reversal.
/// </summary>
public class PayrollRun : AuditableEntity
{
    public string RunNumber { get; private set; } = string.Empty; // system-generated, e.g. "PRL-2026-000003"
    public int PeriodYear { get; private set; }
    public int PeriodMonth { get; private set; }
    public Guid? TreasuryAccountId { get; private set; }
    public string? Notes { get; private set; }
    public PayrollRunStatus Status { get; private set; } = PayrollRunStatus.Draft;

    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? PostedBy { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<PayrollRunLine> _lines = new();
    public IReadOnlyCollection<PayrollRunLine> Lines => _lines.AsReadOnly();

    private PayrollRun() { } // EF Core

    public PayrollRun(string runNumber, int periodYear, int periodMonth, string createdBy,
        Guid? treasuryAccountId = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(runNumber))
            throw new ArgumentException("Payroll run number is required.", nameof(runNumber));
        if (periodMonth is < 1 or > 12)
            throw new DomainException("The payroll period month must be between 1 and 12.");
        if (periodYear is < 2000 or > 2200)
            throw new DomainException("The payroll period year is out of range.");

        RunNumber = runNumber;
        PeriodYear = periodYear;
        PeriodMonth = periodMonth;
        TreasuryAccountId = treasuryAccountId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status == PayrollRunStatus.Draft;

    public decimal TotalGross => _lines.Sum(l => l.GrossPay);
    public decimal TotalDeductions => _lines.Sum(l => l.Deductions);
    public decimal TotalNet => _lines.Sum(l => l.NetPay);

    public void SetTreasuryAccount(Guid? treasuryAccountId, string modifiedBy)
    {
        EnsureEditable();
        TreasuryAccountId = treasuryAccountId;
        Touch(modifiedBy);
    }

    public void SetNotes(string? notes, string modifiedBy)
    {
        EnsureEditable();
        Notes = notes;
        Touch(modifiedBy);
    }

    public PayrollRunLine AddLine(Guid employeeId, string employeeCode, string employeeName, Guid departmentId,
        string? departmentName, decimal basicSalary, decimal allowances, decimal deductions, string? notes = null)
    {
        EnsureEditable();
        if (_lines.Any(l => l.EmployeeId == employeeId))
            throw new DomainException("This employee already appears on this payroll run - a period is never paid twice.");
        if (basicSalary < 0) throw new DomainException("A basic salary cannot be negative.");
        if (allowances < 0 || deductions < 0)
            throw new DomainException("Allowances and deductions cannot be negative.");

        var line = new PayrollRunLine(Id, employeeId, employeeCode, employeeName, departmentId,
            departmentName, basicSalary, allowances, deductions, notes);
        _lines.Add(line);
        Touch("system");
        return line;
    }

    public void UpdateLineAmounts(Guid lineId, decimal allowances, decimal deductions, string? notes, string modifiedBy)
    {
        EnsureEditable();
        var line = FindLine(lineId);
        if (allowances < 0 || deductions < 0)
            throw new DomainException("Allowances and deductions cannot be negative.");

        line.SetAmounts(allowances, deductions, notes);
        Touch(modifiedBy);
    }

    public void RemoveLine(Guid lineId, string modifiedBy)
    {
        EnsureEditable();
        _lines.Remove(FindLine(lineId));
        Touch(modifiedBy);
    }

    public void Approve(string approvedBy)
    {
        if (Status != PayrollRunStatus.Draft)
            throw new DomainException("Only a draft payroll run can be approved.");
        if (_lines.Count == 0)
            throw new DomainException("A payroll run with no employees cannot be approved.");

        Status = PayrollRunStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        Touch(approvedBy);
    }

    public void Post(string postedBy)
    {
        if (Status != PayrollRunStatus.Approved)
            throw new DomainException("Only an approved payroll run can be posted.");
        if (!TreasuryAccountId.HasValue || TreasuryAccountId.Value == Guid.Empty)
            throw new DomainException("A treasury/bank account must be selected before a payroll run is posted.");

        Status = PayrollRunStatus.Posted;
        PostedBy = postedBy;
        PostedAtUtc = DateTime.UtcNow;
        Touch(postedBy);
    }

    public void Cancel(string cancelledBy, string reason)
    {
        if (Status == PayrollRunStatus.Cancelled) return;
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");

        Status = PayrollRunStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        Touch(cancelledBy);
    }

    private PayrollRunLine FindLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException($"Payroll line ({lineId}) does not belong to run {RunNumber}.");

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DocumentLockedException("Payroll Run", RunNumber);
    }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>One employee's pay for one payroll period (spec section 36).</summary>
public class PayrollRunLine : BaseEntity
{
    public Guid PayrollRunId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public Guid DepartmentId { get; private set; }
    public string? DepartmentName { get; private set; }
    public decimal BasicSalary { get; private set; }
    public decimal Allowances { get; private set; }
    public decimal Deductions { get; private set; }
    public string? Notes { get; private set; }

    public decimal GrossPay => BasicSalary + Allowances;
    public decimal NetPay => GrossPay - Deductions;

    private PayrollRunLine() { } // EF Core

    internal PayrollRunLine(Guid payrollRunId, Guid employeeId, string employeeCode, string employeeName,
        Guid departmentId, string? departmentName, decimal basicSalary, decimal allowances, decimal deductions, string? notes)
    {
        PayrollRunId = payrollRunId;
        EmployeeId = employeeId;
        EmployeeCode = employeeCode;
        EmployeeName = employeeName;
        DepartmentId = departmentId;
        DepartmentName = departmentName;
        BasicSalary = basicSalary;
        Allowances = allowances;
        Deductions = deductions;
        Notes = notes;
    }

    internal void SetAmounts(decimal allowances, decimal deductions, string? notes)
    {
        Allowances = allowances;
        Deductions = deductions;
        Notes = notes;
    }
}
