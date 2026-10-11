using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>Everything one employee's payroll line is derived from, in one immutable bundle.</summary>
public record PayrollCalculationInput(
    Employee Employee,
    AttendanceRule Rule,
    DateTime PeriodFrom,
    DateTime PeriodTo,
    IReadOnlyList<AttendanceEvent> Attendance,
    IReadOnlyList<OvertimeEntry> Overtime,
    IReadOnlyList<EmployeeAdvance> Advances,
    IReadOnlyList<(PayComponent Component, EmployeePayComponent Assignment)> Components,
    IReadOnlyList<ContributionRule> Contributions,
    IReadOnlySet<Guid> PaidLeaveRequestIds);

/// <summary>
/// The payroll calculation (spec sections 35-36). It is deliberately a pure
/// function of the configured masters - attendance, overtime, advances, pay
/// components and contribution rules - so no factory-specific amount or rate is
/// ever coded here; changing the rules changes the payroll, which is the whole
/// requirement.
/// </summary>
public static class PayrollCalculator
{
    public static PayrollLineAmounts Calculate(PayrollCalculationInput input)
    {
        var employee = input.Employee;
        var rule = input.Rule;
        var basicSalary = employee.BasicSalary;

        var periodAttendance = input.Attendance
            .Where(a => a.WorkDate >= input.PeriodFrom.Date && a.WorkDate <= input.PeriodTo.Date)
            .ToList();

        var presentDays = periodAttendance.Count(a => a.Status is AttendanceStatus.Present or AttendanceStatus.Late);
        var leaveDays = periodAttendance.Count(a => a.Status == AttendanceStatus.OnLeave);
        var paidLeaveDays = periodAttendance.Count(a => a.Status == AttendanceStatus.OnLeave
            && a.LeaveRequestId.HasValue && input.PaidLeaveRequestIds.Contains(a.LeaveRequestId.Value));
        var absenceDays = periodAttendance.Sum(a => a.AbsenceDays);
        var lateMinutes = periodAttendance.Sum(a => a.LateMinutes);
        var earlyLeaveMinutes = periodAttendance.Sum(a => a.EarlyLeaveMinutes);

        // Days that count for a "per attendance day" component: a paid leave can be
        // configured to count as attendance, an unpaid one never does.
        var attendanceDays = presentDays + (rule.PaidLeaveCountsAsAttendance ? paidLeaveDays : 0);

        // Approved overtime that has not been paid yet by another run.
        var overtimeEntries = input.Overtime
            .Where(o => o.Status == OvertimeStatus.Approved && o.PayrollRunId is null
                        && o.WorkDate >= input.PeriodFrom.Date && o.WorkDate <= input.PeriodTo.Date)
            .ToList();
        var overtimeHours = overtimeEntries.Sum(o => o.Hours);
        var overtimeAmount = overtimeEntries.Sum(o => Math.Round(o.Hours * rule.HourlyRate(basicSalary) * o.ApprovedMultiplier, 4));

        var dailyRate = rule.StandardWorkingDaysPerMonth <= 0 ? 0 : basicSalary / rule.StandardWorkingDaysPerMonth;

        // ---- configured components: percentages of gross need the earnings first, so
        // they are applied in two passes rather than in whatever order the rows arrive.
        var earnings = new List<(PayComponent Component, decimal Amount)>();
        var deductions = new List<(PayComponent Component, decimal Amount)>();
        var percentOfGross = new List<(PayComponent Component, decimal AmountBase, decimal Percent)>();

        foreach (var (component, assignment) in input.Components)
        {
            if (!component.IsActive || !assignment.IsEffectiveOn(input.PeriodTo)) continue;

            switch (component.Calculation)
            {
                case PayComponentCalculation.FixedAmount:
                    Evaluate(component, assignment.Amount ?? component.DefaultAmount, 0m, 0m);
                    break;
                case PayComponentCalculation.PercentOfBasic:
                    Evaluate(component, basicSalary * (assignment.Percent ?? component.DefaultPercent) / 100m, 0m, 0m);
                    break;
                case PayComponentCalculation.PerAttendanceDay:
                    Evaluate(component, (assignment.Amount ?? component.DefaultAmount) * attendanceDays, 0m, 0m);
                    break;
                case PayComponentCalculation.PerOvertimeHour:
                    Evaluate(component, (assignment.Amount ?? component.DefaultAmount) * overtimeHours, 0m, 0m);
                    break;
                case PayComponentCalculation.PercentOfGross:
                    // Base is resolved after the additive earnings are known.
                    percentOfGross.Add((component, 0m, assignment.Percent ?? component.DefaultPercent));
                    break;
            }
        }

        void Evaluate(PayComponent component, decimal amount, decimal _, decimal __)
        {
            if (amount < 0) amount = 0;
            if (component.IsDeduction) deductions.Add((component, Math.Round(amount, 4)));
            else earnings.Add((component, Math.Round(amount, 4)));
        }

        var additiveEarnings = earnings.Sum(e => e.Amount);
        var grossBeforePercentage = basicSalary + additiveEarnings + overtimeAmount;

        foreach (var (component, _, percent) in percentOfGross)
        {
            var amount = Math.Round(grossBeforePercentage * percent / 100m, 4);
            if (component.IsDeduction) deductions.Add((component, amount));
            else earnings.Add((component, amount));
        }

        decimal Sum(PayComponentKind kind) => earnings.Where(e => e.Component.Kind == kind).Sum(e => e.Amount);
        var allowances = Sum(PayComponentKind.Allowance);
        var incentives = Sum(PayComponentKind.Incentive);
        var bonuses = Sum(PayComponentKind.Bonus);
        var commissions = Sum(PayComponentKind.Commission);

        var componentDeductions = deductions.Sum(d => d.Amount);

        // Penalties: absence is charged as a fraction of a day's pay, lateness and
        // early leave at the configured per-minute rate. All three are data.
        var absencePenalty = Math.Round(absenceDays * dailyRate * rule.AbsenceDayDeductionFactor, 4);
        var latePenalty = Math.Round(lateMinutes * rule.LatePenaltyPerMinute, 4);
        var earlyLeavePenalty = Math.Round(earlyLeaveMinutes * rule.EarlyLeavePenaltyPerMinute, 4);
        var totalDeductions = Math.Round(componentDeductions + absencePenalty + latePenalty + earlyLeavePenalty, 4);

        // ---- statutory contributions (spec section 36): configurable rates, optional
        // per-employee exemption, and an insurable floor/ceiling on the rule itself.
        var taxableBase = basicSalary + overtimeAmount
            + earnings.Where(e => e.Component.IsTaxable).Sum(e => e.Amount);
        var insuredBase = basicSalary + overtimeAmount
            + earnings.Where(e => e.Component.IsInsured).Sum(e => e.Amount);

        decimal tax = 0m, insurance = 0m, employerInsurance = 0m;
        foreach (var contribution in input.Contributions)
        {
            if (!contribution.IsActive) continue;
            if (contribution.Kind == ContributionKind.Tax && employee.IsTaxExempt) continue;
            if (contribution.Kind == ContributionKind.Insurance && employee.IsInsuranceExempt) continue;

            var rawBase = contribution.Base == ContributionBase.BasicSalary
                ? basicSalary
                : contribution.Kind == ContributionKind.Tax ? taxableBase : insuredBase;

            if (contribution.Kind == ContributionKind.Tax) tax += contribution.EmployeeContribution(rawBase);
            else insurance += contribution.EmployeeContribution(rawBase);

            employerInsurance += contribution.EmployerContribution(rawBase);
        }

        // ---- advance instalments: the scheduled amount, capped by what still remains.
        var advanceRecovery = input.Advances
            .Where(a => a.Status == AdvanceStatus.Active && a.RemainingAmount > 0)
            .Sum(a => Math.Min(a.InstalmentAmount, a.RemainingAmount));

        return new PayrollLineAmounts(
            BasicSalary: basicSalary,
            Allowances: allowances,
            Incentives: incentives,
            Bonuses: bonuses,
            Commissions: commissions,
            OvertimeAmount: Math.Round(overtimeAmount, 4),
            OtherEarnings: 0m,
            Deductions: totalDeductions,
            Tax: Math.Round(tax, 4),
            Insurance: Math.Round(insurance, 4),
            EmployerInsurance: Math.Round(employerInsurance, 4),
            Advances: Math.Round(advanceRecovery, 4),
            AttendanceDays: attendanceDays,
            OvertimeHours: overtimeHours,
            LeaveDays: leaveDays);
    }
}
