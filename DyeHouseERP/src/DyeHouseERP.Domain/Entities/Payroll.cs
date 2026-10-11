using System.ComponentModel.DataAnnotations;
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

    // ---- HR classification (spec sections 28-34). All optional configuration: a
    // factory that does not use sections/cost centers simply leaves them empty, and
    // an employee without a rostered HR shift still has attendance recorded manually.
    public Guid? HrSectionId { get; private set; }
    public Guid? JobPositionId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public Guid? HrShiftId { get; private set; }

    /// <summary>Statutory exemption flags - a configured exemption, not a computation.</summary>
    public bool IsTaxExempt { get; private set; }
    public bool IsInsuranceExempt { get; private set; }

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

    /// <summary>
    /// Sets the employee's HR classification (section, job, cost center, attendance
    /// shift). Every value is optional; passing null clears it.
    /// </summary>
    public void SetHrClassification(Guid? hrSectionId, Guid? jobPositionId, Guid? costCenterId, Guid? hrShiftId,
        string modifiedBy)
    {
        HrSectionId = hrSectionId;
        JobPositionId = jobPositionId;
        CostCenterId = costCenterId;
        HrShiftId = hrShiftId;
        Touch(modifiedBy);
    }

    /// <summary>Sets the statutory exemption flags (spec section 36). An exempt employee is skipped by the matching contribution rule.</summary>
    public void SetTaxInsuranceExemption(bool isTaxExempt, bool isInsuranceExempt, string modifiedBy)
    {
        IsTaxExempt = isTaxExempt;
        IsInsuranceExempt = isInsuranceExempt;
        Touch(modifiedBy);
    }

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

    // ---- calculation / review workflow (spec section 39) ----
    public string? CalculatedBy { get; private set; }
    public DateTime? CalculatedAtUtc { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewNotes { get; private set; }

    // ---- reversal traceability (spec section 39): a posted run is never edited,
    // it is REVERSED by a linked adjustment run that carries the corrections. ----
    public string? ReversedBy { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public string? ReversalReason { get; private set; }

    /// <summary>Set on the ADJUSTMENT run: the posted run it corrects.</summary>
    public Guid? ReversesPayrollRunId { get; private set; }

    /// <summary>Set on the ORIGINAL run: the adjustment run that reversed it.</summary>
    public Guid? ReversedByPayrollRunId { get; private set; }

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. A payroll run
    /// moves real money out of the treasury, so approval/posting/cancellation
    /// must be mutually exclusive at the database level.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

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

    /// <summary>Editable while the amounts can still change: Draft and Calculated (before submission).</summary>
    public bool IsEditable => Status is PayrollRunStatus.Draft or PayrollRunStatus.Calculated;

    public decimal TotalGross => _lines.Sum(l => l.GrossPay);
    public decimal TotalDeductions => _lines.Sum(l => l.Deductions);
    public decimal TotalNet => _lines.Sum(l => l.NetPay);
    public decimal TotalTax => _lines.Sum(l => l.Tax);
    public decimal TotalInsurance => _lines.Sum(l => l.Insurance);
    public decimal TotalEmployerInsurance => _lines.Sum(l => l.EmployerInsurance);
    public decimal TotalAdvances => _lines.Sum(l => l.Advances);
    public decimal TotalOvertime => _lines.Sum(l => l.OvertimeAmount);

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
        string? departmentName, decimal basicSalary, decimal allowances, decimal deductions, string? notes = null,
        Guid? costCenterId = null, Guid? hrSectionId = null)
    {
        EnsureEditable();
        if (_lines.Any(l => l.EmployeeId == employeeId))
            throw new DomainException("This employee already appears on this payroll run - a period is never paid twice.");
        if (basicSalary < 0) throw new DomainException("A basic salary cannot be negative.");
        if (allowances < 0 || deductions < 0)
            throw new DomainException("Allowances and deductions cannot be negative.");

        var line = new PayrollRunLine(Id, employeeId, employeeCode, employeeName, departmentId,
            departmentName, basicSalary, allowances, deductions, notes, costCenterId, hrSectionId);
        _lines.Add(line);
        Touch("system");
        return line;
    }

    /// <summary>
    /// Applies a calculated set of amounts to the run's lines (spec sections 35-39).
    /// Only the lines named in the bundle are touched; the run moves to Calculated.
    /// </summary>
    public void MarkCalculated(IReadOnlyDictionary<Guid, PayrollLineAmounts> amountsByLineId,
        IReadOnlyDictionary<Guid, Guid?> costCentersByLineId, IReadOnlyDictionary<Guid, Guid?> sectionsByLineId,
        string calculatedBy)
    {
        EnsureEditable();

        foreach (var line in _lines)
        {
            if (!amountsByLineId.TryGetValue(line.Id, out var amounts)) continue;
            line.ApplyCalculatedAmounts(amounts);
            line.SetClassification(costCentersByLineId.GetValueOrDefault(line.Id), sectionsByLineId.GetValueOrDefault(line.Id));
        }

        Status = PayrollRunStatus.Calculated;
        CalculatedBy = calculatedBy;
        CalculatedAtUtc = DateTime.UtcNow;
        Touch(calculatedBy);
    }

    /// <summary>
    /// Copies one line into this run (spec section 39) - the adjustment run carries
    /// the original figures so they can be corrected and re-posted. The copy belongs
    /// to THIS run; nothing is shared with the original.
    /// </summary>
    public PayrollRunLine CopyLineFrom(PayrollRunLine source, string createdBy)
    {
        EnsureEditable();
        if (source is null) throw new DomainException("There is no payroll line to copy.");
        if (_lines.Any(l => l.EmployeeId == source.EmployeeId))
            throw new DomainException("This employee already appears on this payroll run - a period is never paid twice.");

        var copy = new PayrollRunLine(Id, source.EmployeeId, source.EmployeeCode, source.EmployeeName,
            source.DepartmentId, source.DepartmentName, source.BasicSalary, source.Allowances, source.Deductions,
            source.Notes, source.CostCenterId, source.HrSectionId);
        copy.ApplyCalculatedAmounts(new PayrollLineAmounts(
            source.BasicSalary, source.Allowances, source.Incentives, source.Bonuses, source.Commissions,
            source.OvertimeAmount, source.OtherEarnings, source.Deductions, source.Tax, source.Insurance,
            source.EmployerInsurance, source.Advances, source.AttendanceDays, source.OvertimeHours, source.LeaveDays));
        _lines.Add(copy);
        Touch(createdBy);
        return copy;
    }

    /// <summary>Submits a calculated run for review (spec section 39).</summary>
    public void SubmitForReview(string submittedBy)
    {
        if (Status != PayrollRunStatus.Calculated)
            throw new DomainException("Only a calculated payroll run can be submitted for review.");

        Status = PayrollRunStatus.UnderReview;
        SubmittedBy = submittedBy;
        SubmittedAtUtc = DateTime.UtcNow;
        Touch(submittedBy);
    }

    /// <summary>Returns a submitted run to the calculated stage so a line can be corrected (spec section 39).</summary>
    public void ReturnToCalculated(string reviewedBy, string? notes)
    {
        if (Status != PayrollRunStatus.UnderReview)
            throw new DomainException("Only a run that is under review can be returned for correction.");

        Status = PayrollRunStatus.Calculated;
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch(reviewedBy);
    }

    /// <summary>Links an ADJUSTMENT run to the posted run it corrects (spec section 39).</summary>
    public void LinkReversal(Guid reversesPayrollRunId)
    {
        if (reversesPayrollRunId == Guid.Empty)
            throw new DomainException("An adjustment run must name the payroll run it reverses.");

        ReversesPayrollRunId = reversesPayrollRunId;
    }

    /// <summary>
    /// Reverses a POSTED run (spec section 39). Nothing is deleted or edited: the run
    /// is marked Reversed with the reason and a link to the adjustment run that now
    /// carries the correction. Only a posted run can be reversed, and a reason is
    /// mandatory.
    /// </summary>
    public void Reverse(string reversedBy, string reason, Guid adjustmentRunId)
    {
        if (Status != PayrollRunStatus.Posted)
            throw new DomainException("Only a posted payroll run can be reversed.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A reversal reason is required.");
        if (adjustmentRunId == Guid.Empty)
            throw new DomainException("A reversal must name the adjustment run that corrects it.");

        Status = PayrollRunStatus.Reversed;
        ReversedBy = reversedBy;
        ReversedAtUtc = DateTime.UtcNow;
        ReversalReason = reason.Trim();
        ReversedByPayrollRunId = adjustmentRunId;
        Touch(reversedBy);
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
        // A plain draft run can still be approved directly (the simple path), and a
        // calculated/under-review run moves through the same door - but an approved,
        // posted, cancelled or reversed run is never re-approved.
        if (Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Calculated or PayrollRunStatus.UnderReview))
            throw new DomainException($"A {Status} payroll run can no longer be approved.");
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
        if (Status == PayrollRunStatus.Reversed)
            throw new DomainException("A reversed payroll run cannot be cancelled - the reversal is the correction.");
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

/// <summary>
/// One employee's pay for one payroll period (spec sections 35-36). The line
/// carries the full breakdown the calculator produced - earnings, statutory
/// contributions, recovered advances and the attendance figures they were derived
/// from - so a payslip and the accounting export never need to recompute anything.
/// </summary>
public class PayrollRunLine : BaseEntity
{
    public Guid PayrollRunId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public Guid DepartmentId { get; private set; }
    public string? DepartmentName { get; private set; }

    /// <summary>Classification snapshotted at calculation time, for labor-cost reporting.</summary>
    public Guid? CostCenterId { get; private set; }
    public Guid? HrSectionId { get; private set; }

    // ---- earnings ----
    public decimal BasicSalary { get; private set; }
    public decimal Allowances { get; private set; }
    public decimal Incentives { get; private set; }
    public decimal Bonuses { get; private set; }
    public decimal Commissions { get; private set; }
    public decimal OvertimeAmount { get; private set; }
    public decimal OtherEarnings { get; private set; }

    // ---- deductions & statutory contributions ----
    public decimal Deductions { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Insurance { get; private set; }

    /// <summary>The EMPLOYER's statutory share - a cost to the factory, not deducted from the employee.</summary>
    public decimal EmployerInsurance { get; private set; }

    /// <summary>Advance instalment recovered this period.</summary>
    public decimal Advances { get; private set; }

    // ---- the attendance figures the amounts were derived from ----
    public decimal AttendanceDays { get; private set; }
    public decimal OvertimeHours { get; private set; }
    public decimal LeaveDays { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Everything earned before deductions and contributions.</summary>
    public decimal GrossPay => BasicSalary + Allowances + Incentives + Bonuses + Commissions + OvertimeAmount + OtherEarnings;

    /// <summary>What actually reaches the employee: gross minus deductions, tax, insurance and recovered advances.</summary>
    public decimal NetPay => GrossPay - Deductions - Tax - Insurance - Advances;

    private PayrollRunLine() { } // EF Core

    internal PayrollRunLine(Guid payrollRunId, Guid employeeId, string employeeCode, string employeeName,
        Guid departmentId, string? departmentName, decimal basicSalary, decimal allowances, decimal deductions,
        string? notes, Guid? costCenterId = null, Guid? hrSectionId = null)
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
        CostCenterId = costCenterId;
        HrSectionId = hrSectionId;
    }

    /// <summary>A manual correction of the two hand-editable amounts (spec section 36).</summary>
    internal void SetAmounts(decimal allowances, decimal deductions, string? notes)
    {
        Allowances = allowances;
        Deductions = deductions;
        Notes = notes;
    }

    /// <summary>Overwrites the line with a freshly calculated breakdown (spec sections 35-36).</summary>
    internal void ApplyCalculatedAmounts(PayrollLineAmounts a)
    {
        BasicSalary = a.BasicSalary;
        Allowances = a.Allowances;
        Incentives = a.Incentives;
        Bonuses = a.Bonuses;
        Commissions = a.Commissions;
        OvertimeAmount = a.OvertimeAmount;
        OtherEarnings = a.OtherEarnings;
        Deductions = a.Deductions;
        Tax = a.Tax;
        Insurance = a.Insurance;
        EmployerInsurance = a.EmployerInsurance;
        Advances = a.Advances;
        AttendanceDays = a.AttendanceDays;
        OvertimeHours = a.OvertimeHours;
        LeaveDays = a.LeaveDays;
    }

    /// <summary>Snapshots the employee's classification for labor-cost reporting.</summary>
    internal void SetClassification(Guid? costCenterId, Guid? hrSectionId)
    {
        CostCenterId = costCenterId;
        HrSectionId = hrSectionId;
    }
}
