namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// One employee's calculated payroll line for one period (spec sections 35-36).
/// Immutable by design: the calculator constructs it once per line, and it is
/// consumed by the payroll lifecycle commands and the HR reporting queries.
///
/// It lives in the Domain (not the Application) because <see cref="PayrollRun"/>
/// owns the calculation result: the run takes a set of these and applies them to
/// its own lines, so the Domain must be able to name the type.
/// </summary>
public class PayrollLineAmounts
{
    public PayrollLineAmounts(
        decimal BasicSalary,
        decimal Allowances,
        decimal Incentives,
        decimal Bonuses,
        decimal Commissions,
        decimal OvertimeAmount,
        decimal OtherEarnings,
        decimal Deductions,
        decimal Tax,
        decimal Insurance,
        decimal EmployerInsurance,
        decimal Advances,
        decimal AttendanceDays,
        decimal OvertimeHours,
        decimal LeaveDays = 0m)
    {
        this.BasicSalary = BasicSalary;
        this.Allowances = Allowances;
        this.Incentives = Incentives;
        this.Bonuses = Bonuses;
        this.Commissions = Commissions;
        this.OvertimeAmount = OvertimeAmount;
        this.OtherEarnings = OtherEarnings;
        this.Deductions = Deductions;
        this.Tax = Tax;
        this.Insurance = Insurance;
        this.EmployerInsurance = EmployerInsurance;
        this.Advances = Advances;
        this.AttendanceDays = AttendanceDays;
        this.OvertimeHours = OvertimeHours;
        this.LeaveDays = LeaveDays;
    }

    /// <summary>Employee's base salary for the period.</summary>
    public decimal BasicSalary { get; }

    /// <summary>Sum of allowance-type components.</summary>
    public decimal Allowances { get; }

    /// <summary>Sum of incentive-type components.</summary>
    public decimal Incentives { get; }

    /// <summary>Sum of bonus-type components.</summary>
    public decimal Bonuses { get; }

    /// <summary>Sum of commission-type components.</summary>
    public decimal Commissions { get; }

    /// <summary>Approved overtime, converted to money.</summary>
    public decimal OvertimeAmount { get; }

    /// <summary>Reserved for future additional earnings categories.</summary>
    public decimal OtherEarnings { get; }

    /// <summary>Component deductions plus statutory absence/late/early penalties.</summary>
    public decimal Deductions { get; }

    /// <summary>Employee statutory tax for the period.</summary>
    public decimal Tax { get; }

    /// <summary>Employee statutory insurance for the period.</summary>
    public decimal Insurance { get; }

    /// <summary>Employer statutory insurance for the period.</summary>
    public decimal EmployerInsurance { get; }

    /// <summary>Active advance instalments recovered this period.</summary>
    public decimal Advances { get; }

    /// <summary>Attendance days used for per-day components.</summary>
    public decimal AttendanceDays { get; }

    /// <summary>Approved overtime hours for the period.</summary>
    public decimal OvertimeHours { get; }

    /// <summary>Leave days recorded for the period.</summary>
    public decimal LeaveDays { get; }
}
