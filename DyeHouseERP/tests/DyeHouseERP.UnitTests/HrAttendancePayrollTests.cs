using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 28-31: HR organisation and attendance. Shifts are DATA (including
/// the working week), an HR shift is deliberately separate from a production
/// execution shift, and lateness/early-leave/overtime are derived from the shift and
/// the policy - never typed by the client.
/// </summary>
public class HrShiftAndAttendanceTests
{
    private static AttendanceRule Rule() => new("Standard", "tester");

    private static HrShift MorningShift(int graceMinutes = 10)
        => new("M", 8 * 60, 16 * 60, "tester", "صباحي", "Morning", breakMinutes: 0, graceMinutes: graceMinutes);

    [Fact]
    public void AShift_RequiresSensibleTiming()
    {
        Action sameStartEnd = () => new HrShift("M", 480, 480, "tester", "صباحي", "Morning");
        sameStartEnd.Should().Throw<DomainException>("a shift cannot start and end at the same minute");

        Action badWorkingDays = () => new HrShift("M", 480, 960, "tester", "صباحي", "Morning", workingDays: "9,10");
        badWorkingDays.Should().Throw<DomainException>("a shift must have at least one real working day");
    }

    [Fact]
    public void ANightShift_CrossesMidnight_AndItsPlannedMinutesAccountForTheBreak()
    {
        var night = new HrShift("N", 22 * 60, 6 * 60, "tester", "ليلي", "Night", breakMinutes: 30);

        night.IsNightShift.Should().BeTrue();
        night.PlannedMinutes.Should().Be(8 * 60 - 30, "22:00 to 06:00 is 8 hours, minus a 30 minute break");

        var custom = new HrShift("W", 8 * 60, 16 * 60, "tester", "خاص", "Custom", workingDays: "0,1");
        custom.WorksOn(DayOfWeek.Sunday).Should().BeTrue("the working week is configurable, not fixed");
        custom.WorksOn(DayOfWeek.Wednesday).Should().BeFalse();
    }

    [Fact]
    public void Lateness_IsOnlyCountedBeyondTheShiftsOwnGrace()
    {
        var shift = MorningShift(graceMinutes: 10);
        var rule = Rule();

        var withinGrace = new AttendanceEvent(Guid.NewGuid(), DateTime.UtcNow.Date, shift, rule,
            checkInMinutes: 8 * 60 + 8, checkOutMinutes: 16 * 60, AttendanceSource.Manual, "tester");
        withinGrace.Status.Should().Be(AttendanceStatus.Present);
        withinGrace.LateMinutes.Should().Be(0);

        var late = new AttendanceEvent(Guid.NewGuid(), DateTime.UtcNow.Date, shift, rule,
            checkInMinutes: 8 * 60 + 20, checkOutMinutes: 16 * 60, AttendanceSource.Manual, "tester");
        late.Status.Should().Be(AttendanceStatus.Late);
        late.LateMinutes.Should().Be(20, "the whole delay counts once the grace is passed");
    }

    [Fact]
    public void EarlyLeave_And_Overtime_AreDerivedFromTheShiftEnd()
    {
        var shift = MorningShift();
        var rule = new AttendanceRule("Standard", "tester");
        rule.Configure(8m, 26m, 0, 0m, 0m, 1m, overtimeGraceMinutes: 15,
            1.5m, 2m, 2.5m, null, true, true);

        var leftEarly = new AttendanceEvent(Guid.NewGuid(), DateTime.UtcNow.Date, shift, rule,
            checkInMinutes: 8 * 60, checkOutMinutes: 15 * 60 + 30, AttendanceSource.Manual, "tester");
        leftEarly.EarlyLeaveMinutes.Should().Be(30);
        leftEarly.OvertimeMinutes.Should().Be(0);

        var stayedLate = new AttendanceEvent(Guid.NewGuid(), DateTime.UtcNow.Date, shift, rule,
            checkInMinutes: 8 * 60, checkOutMinutes: 17 * 60 + 45, AttendanceSource.Manual, "tester");
        stayedLate.OvertimeMinutes.Should().Be(90, "1h45m past the end, minus a 15 minute overtime grace");
        stayedLate.EarlyLeaveMinutes.Should().Be(0);
    }

    [Fact]
    public void AnAbsence_CarriesADeductibleDay_AndAnApprovedLeave_MarksTheAttendanceRow()
    {
        var shift = MorningShift();
        var rule = Rule();

        var absent = AttendanceEvent.NonWorking(Guid.NewGuid(), DateTime.UtcNow.Date, AttendanceStatus.Absent,
            shift, rule, "tester");
        absent.AbsenceDays.Should().Be(1m);

        var leaveId = Guid.NewGuid();
        var onLeave = AttendanceEvent.NonWorking(Guid.NewGuid(), DateTime.UtcNow.Date, AttendanceStatus.OnLeave,
            shift, rule, "tester", leaveRequestId: leaveId);
        onLeave.AbsenceDays.Should().Be(0m, "an approved leave is never an absence");
        onLeave.LeaveRequestId.Should().Be(leaveId);

        var worked = new AttendanceEvent(Guid.NewGuid(), DateTime.UtcNow.Date, shift, rule,
            8 * 60, 16 * 60, AttendanceSource.Manual, "tester");
        worked.MarkOnLeave(leaveId, "approver");

        worked.Status.Should().Be(AttendanceStatus.OnLeave);
        worked.WorkedMinutes.Should().Be(0, "the day becomes the approved leave");
    }
}

/// <summary>
/// Spec sections 32-34: leave, overtime and advances are real workflows - a request
/// is decided by a DIFFERENT user, a rejection needs a reason, an approved overtime
/// entry snapshots its multiplier, and an advance is recovered in instalments.
/// </summary>
public class HrWorkflowTests
{
    [Fact]
    public void ALeaveRequest_RequiresAReason_AndCanBeDecidedOnlyWhilePending()
    {
        var request = new LeaveRequest(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(2), 3m, "family", "clerk");

        Action noReason = () => new LeaveRequest(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.Date,
            DateTime.UtcNow.Date, 1m, "  ", "clerk");
        noReason.Should().Throw<DomainException>();

        request.Approve("manager", DateTime.UtcNow);
        request.Status.Should().Be(LeaveStatus.Approved);

        Action twice = () => request.Approve("someone-else", DateTime.UtcNow);
        twice.Should().Throw<DomainException>("a decided request is never re-decided");

        var rejected = new LeaveRequest(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.Date,
            DateTime.UtcNow.Date, 1m, "sick", "clerk");
        Action rejectWithoutReason = () => rejected.Reject("manager", "", DateTime.UtcNow);
        rejectWithoutReason.Should().Throw<DomainException>();
    }

    [Fact]
    public void CancellingAnApprovedLeave_KeepsTheDecisionHistory()
    {
        var request = new LeaveRequest(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.Date,
            DateTime.UtcNow.Date, 1m, "family", "clerk");
        request.Approve("manager", DateTime.UtcNow);
        request.MarkAttendanceApplied("manager");

        request.Cancel("clerk", "plans changed");

        request.Status.Should().Be(LeaveStatus.Cancelled);
        request.DecidedBy.Should().Be("manager", "who approved it is still part of the audit trail");
        request.AttendanceApplied.Should().BeFalse("the attendance it wrote may be removed");
    }

    [Fact]
    public void AnOvertimeEntry_SnapshotsItsMultiplierOnApproval_AndIsPaidOnlyOnce()
    {
        var entry = new OvertimeEntry(Guid.NewGuid(), DateTime.UtcNow.Date, 3m, OvertimeCategory.Weekend,
            "urgent order", "operator");

        entry.Status.Should().Be(OvertimeStatus.Pending);
        entry.ApprovedMultiplier.Should().Be(0m, "nothing is paid before approval");

        entry.Approve("floor-manager", 2m, DateTime.UtcNow);

        entry.Status.Should().Be(OvertimeStatus.Approved);
        entry.ApprovedMultiplier.Should().Be(2m);

        var runId = Guid.NewGuid();
        entry.MarkPaid(runId, "accountant");
        entry.PayrollRunId.Should().Be(runId);

        Action paidAgainByAnotherRun = () => entry.MarkPaid(Guid.NewGuid(), "accountant");
        paidAgainByAnotherRun.Should().Throw<DomainException>("an hour is never paid twice");

        Action approveAgain = () => entry.Approve("someone", 3m, DateTime.UtcNow);
        approveAgain.Should().Throw<DomainException>();
    }

    [Fact]
    public void AnAdvance_IsRecoveredInInstalments_AndClosesWhenFullyRecovered()
    {
        var advance = new EmployeeAdvance(Guid.NewGuid(), DateTime.UtcNow.Date, 1000m, 4, "loan", "hr");

        advance.InstalmentAmount.Should().Be(250m);
        advance.RemainingAmount.Should().Be(1000m);

        advance.Recover(250m, "payroll");
        advance.Recover(250m, "payroll");
        advance.RemainingAmount.Should().Be(500m);
        advance.Status.Should().Be(AdvanceStatus.Active);

        // Over-recovery is capped at what actually remains.
        var taken = advance.Recover(1000m, "payroll");
        taken.Should().Be(500m);
        advance.RemainingAmount.Should().Be(0m);
        advance.Status.Should().Be(AdvanceStatus.Closed);

        Action cancelClosed = () => advance.Cancel("hr", "no longer needed");
        cancelClosed.Should().Throw<DomainException>();
    }
}

/// <summary>
/// Spec sections 35-36: contribution rules are configuration - rates, base and an
/// insurable floor/ceiling. Nothing about them is factory-specific in code.
/// </summary>
public class ContributionRuleTests
{
    [Fact]
    public void AnInsurableCeiling_ClampsTheBase_AndRatesDeriveFromIt()
    {
        var rule = new ContributionRule("INS", ContributionKind.Insurance, ContributionBase.BasicSalary,
            employeeRatePercent: 5m, employerRatePercent: 10m, createdBy: "hr",
            nameAr: "تأمينات", nameEn: "Insurance", minBase: 1000m, maxBase: 5000m);

        rule.InsurableBase(800m).Should().Be(1000m, "the floor applies");
        rule.InsurableBase(9000m).Should().Be(5000m, "the ceiling applies");
        rule.InsurableBase(4000m).Should().Be(4000m);

        rule.EmployeeContribution(9000m).Should().Be(250m, "5% of the 5000 ceiling");
        rule.EmployerContribution(9000m).Should().Be(500m, "10% of the 5000 ceiling");
    }

    [Fact]
    public void ARateAboveOneHundredPercent_IsRefused()
    {
        Action act = () => new ContributionRule("B", ContributionKind.Tax, ContributionBase.GrossPay, 120m, 0m,
            "hr", "ضريبة", "Tax");
        act.Should().Throw<DomainException>();
    }
}

/// <summary>
/// Spec sections 35-36: the calculation itself. It is a pure function of the
/// configured masters, so these tests pin the arithmetic to simple, exact figures.
/// </summary>
public class PayrollCalculatorTests
{
    private static Employee Employee(decimal basic = 6000m)
        => new("E1", "Ali Hassan", Guid.NewGuid(), basic, DateTime.UtcNow.Date.AddYears(-1), "hr");

    private static AttendanceRule Rule(decimal latePenaltyPerMinute = 0m, decimal absenceFactor = 1m)
    {
        var rule = new AttendanceRule("Standard", "hr");
        rule.Configure(standardDailyHours: 8m, standardWorkingDaysPerMonth: 25m, graceMinutes: 0,
            latePenaltyPerMinute, earlyLeavePenaltyPerMinute: 0m, absenceDayDeductionFactor: absenceFactor,
            overtimeGraceMinutes: 0, 1.5m, 2m, 2.5m, "5,6", true, true);
        return rule;
    }

    [Fact]
    public void AFixedAllowance_AndAConfiguredOvertimeHour_AreAddedToGross()
    {
        var employee = Employee();
        var rule = Rule();
        var component = new PayComponent("HOU", PayComponentKind.Allowance, PayComponentCalculation.FixedAmount,
            "hr", "بدل سكن", "Housing", defaultAmount: 500m);
        var assignment = new EmployeePayComponent(employee.Id, component.Id, DateTime.UtcNow.Date.AddDays(-30), "hr");

        var overtime = new OvertimeEntry(employee.Id, DateTime.UtcNow.Date, 2m, OvertimeCategory.Normal, "order", "op");
        overtime.Approve("floor-manager", 1.5m, DateTime.UtcNow);

        var input = new PayrollCalculationInput(
            employee, rule, DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            Array.Empty<AttendanceEvent>(), new[] { overtime }, Array.Empty<EmployeeAdvance>(),
            new[] { (component, assignment) }, Array.Empty<ContributionRule>(), new HashSet<Guid>());

        var line = PayrollCalculator.Calculate(input);

        line.BasicSalary.Should().Be(6000m);
        line.Allowances.Should().Be(500m);
        line.OvertimeHours.Should().Be(2m);
        // hourly rate = 6000 / (25 x 8) = 30; 2h x 1.5 = 90
        line.OvertimeAmount.Should().Be(90m);
        line.Tax.Should().Be(0m, "no tax rule is configured");
    }

    [Fact]
    public void APercentageOfGross_IsComputedOnTheEarningsBeforeIt()
    {
        var employee = Employee();
        var rule = Rule();

        var housing = new PayComponent("HOU", PayComponentKind.Allowance, PayComponentCalculation.FixedAmount,
            "hr", "بدل سكن", "Housing", defaultAmount: 500m);
        var commission = new PayComponent("COM", PayComponentKind.Commission, PayComponentCalculation.PercentOfGross,
            "hr", "عمولة", "Commission", defaultPercent: 10m);

        var assignments = new List<(PayComponent, EmployeePayComponent)>
        {
            (housing, new EmployeePayComponent(employee.Id, housing.Id, DateTime.UtcNow.Date.AddDays(-30), "hr")),
            (commission, new EmployeePayComponent(employee.Id, commission.Id, DateTime.UtcNow.Date.AddDays(-30), "hr"))
        };

        var input = new PayrollCalculationInput(
            employee, rule, DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            Array.Empty<AttendanceEvent>(), Array.Empty<OvertimeEntry>(), Array.Empty<EmployeeAdvance>(),
            assignments, Array.Empty<ContributionRule>(), new HashSet<Guid>());

        var line = PayrollCalculator.Calculate(input);

        // Base before the percentage = basic 6000 + housing 500 = 6500; 10% = 650.
        line.Commissions.Should().Be(650m);
        (line.BasicSalary + line.Allowances + line.Commissions).Should().Be(7150m);
    }

    [Fact]
    public void Attendance_DrivesThePenalties_AndThePerDayComponent()
    {
        var employee = Employee();
        var rule = Rule(latePenaltyPerMinute: 1m);
        var shift = new HrShift("M", 8 * 60, 16 * 60, "hr", "صباحي", "Morning", graceMinutes: 10);

        var lateDay = new AttendanceEvent(employee.Id, DateTime.UtcNow.Date, shift, rule,
            8 * 60 + 30, 16 * 60, AttendanceSource.Manual, "clerk");
        var absentDay = AttendanceEvent.NonWorking(employee.Id, DateTime.UtcNow.Date.AddDays(-1),
            AttendanceStatus.Absent, shift, rule, "clerk");

        var meal = new PayComponent("MEAL", PayComponentKind.Allowance, PayComponentCalculation.PerAttendanceDay,
            "hr", "بدل وجبة", "Meal", defaultAmount: 10m);
        var assignment = new EmployeePayComponent(employee.Id, meal.Id, DateTime.UtcNow.Date.AddDays(-30), "hr");

        var input = new PayrollCalculationInput(
            employee, rule, DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            new[] { lateDay, absentDay }, Array.Empty<OvertimeEntry>(), Array.Empty<EmployeeAdvance>(),
            new[] { (meal, assignment) }, Array.Empty<ContributionRule>(), new HashSet<Guid>());

        var line = PayrollCalculator.Calculate(input);

        line.AttendanceDays.Should().Be(1m, "one present day");
        line.Allowances.Should().Be(10m, "10 per attendance day");

        // daily rate = 6000 / 25 = 240; one absence = 240; 30 late minutes at 1 per minute = 30.
        line.Deductions.Should().Be(270m);
    }

    [Fact]
    public void TaxAndInsurance_FollowTheirRules_AndAnExemptEmployee_isSkipped()
    {
        var employee = Employee();
        var rule = Rule();

        var tax = new ContributionRule("TAX", ContributionKind.Tax, ContributionBase.GrossPay, 10m, 0m, "hr",
            "ضريبة", "Tax");
        var insurance = new ContributionRule("INS", ContributionKind.Insurance, ContributionBase.BasicSalary,
            5m, 12m, "hr", "تأمينات", "Insurance");

        var taxed = PayrollCalculator.Calculate(new PayrollCalculationInput(
            employee, rule, DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            Array.Empty<AttendanceEvent>(), Array.Empty<OvertimeEntry>(), Array.Empty<EmployeeAdvance>(),
            Array.Empty<(PayComponent, EmployeePayComponent)>(), new[] { tax, insurance }, new HashSet<Guid>()));

        taxed.Tax.Should().Be(600m, "10% of the 6000 gross");
        taxed.Insurance.Should().Be(300m, "5% of the 6000 basic");
        taxed.EmployerInsurance.Should().Be(720m, "12% of the 6000 basic is the employer's share");

        employee.SetTaxInsuranceExemption(isTaxExempt: true, isInsuranceExempt: false, "hr");
        var exempt = PayrollCalculator.Calculate(new PayrollCalculationInput(
            employee, rule, DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            Array.Empty<AttendanceEvent>(), Array.Empty<OvertimeEntry>(), Array.Empty<EmployeeAdvance>(),
            Array.Empty<(PayComponent, EmployeePayComponent)>(), new[] { tax, insurance }, new HashSet<Guid>()));

        exempt.Tax.Should().Be(0m, "the employee is tax exempt");
        exempt.Insurance.Should().Be(300m, "but still insured");
    }

    [Fact]
    public void AnActiveAdvance_DeductsItsScheduledInstalment_WithoutReachingIntoTheEntity()
    {
        var employee = Employee();
        var advance = new EmployeeAdvance(employee.Id, DateTime.UtcNow.Date.AddMonths(-1), 1000m, 4, "loan", "hr");

        var line = PayrollCalculator.Calculate(new PayrollCalculationInput(
            employee, Rule(), DateTime.UtcNow.Date.AddDays(-10), DateTime.UtcNow.Date,
            Array.Empty<AttendanceEvent>(), Array.Empty<OvertimeEntry>(), new[] { advance },
            Array.Empty<(PayComponent, EmployeePayComponent)>(), Array.Empty<ContributionRule>(), new HashSet<Guid>()));

        line.Advances.Should().Be(250m, "the instalment, not the whole loan");
        advance.RecoveredAmount.Should().Be(0m, "calculating never recovers - posting does");
        advance.RemainingAmount.Should().Be(1000m);
    }
}

/// <summary>
/// Spec section 39: Draft -&gt; Calculated -&gt; UnderReview -&gt; Approved -&gt; Posted, and a
/// posted run is REVERSED by a linked adjustment run rather than edited or deleted.
/// </summary>
public class PayrollLifecycleTests
{
    private static PayrollRun Run(Guid? accountId, out Guid lineId)
    {
        var run = new PayrollRun("PRL-2026-000009", 2026, 4, "hr", accountId);
        var line = run.AddLine(Guid.NewGuid(), "EMP-001", "Ali Hassan", Guid.NewGuid(), "Dyeing", 6000m, 0m, 0m);
        lineId = line.Id;
        return run;
    }

    [Fact]
    public void CalculateReviewAndApprove_IsTheNewPath_ButDraftApprovalStillWorks()
    {
        var run = Run(Guid.NewGuid(), out var lineId);
        var amounts = new Dictionary<Guid, PayrollLineAmounts>
        {
            [lineId] = new(6000m, 500m, 0m, 0m, 0m, 90m, 0m, 0m, 600m, 300m, 720m, 250m, 22m, 2m)
        };

        run.MarkCalculated(amounts, new Dictionary<Guid, Guid?> { [lineId] = null },
            new Dictionary<Guid, Guid?> { [lineId] = null }, "accountant");
        run.Status.Should().Be(PayrollRunStatus.Calculated);
        run.Lines.Single().NetPay.Should().Be(6000m + 500m + 90m - 600m - 300m - 250m);

        run.SubmitForReview("accountant");
        run.Status.Should().Be(PayrollRunStatus.UnderReview);

        run.ReturnToCalculated("hr-manager", "recheck attendance");
        run.Status.Should().Be(PayrollRunStatus.Calculated);
        run.ReviewNotes.Should().Be("recheck attendance");

        run.SubmitForReview("accountant");
        run.Approve("hr-manager");
        run.Status.Should().Be(PayrollRunStatus.Approved);

        run.Post("accountant");
        run.Status.Should().Be(PayrollRunStatus.Posted);
    }

    [Fact]
    public void AReversedRun_IsNeverCancelled_AndItsCorrectionIsAnAdjustmentRun()
    {
        var accountId = Guid.NewGuid();
        var run = Run(accountId, out _);
        run.Approve("hr-manager");
        run.Post("accountant");

        var adjustment = new PayrollRun("PRL-2026-000010", 2026, 4, "accountant", accountId, "correction");
        adjustment.LinkReversal(run.Id);

        run.Reverse("accountant", "wrong salary applied", adjustment.Id);

        run.Status.Should().Be(PayrollRunStatus.Reversed);
        run.ReversalReason.Should().Be("wrong salary applied");
        run.ReversedByPayrollRunId.Should().Be(adjustment.Id);
        adjustment.ReversesPayrollRunId.Should().Be(run.Id);

        Action cancelReversed = () => run.Cancel("accountant", "no need");
        cancelReversed.Should().Throw<DomainException>("the reversal is the correction, not a cancellation");
    }

    [Fact]
    public void AnAdjustmentRun_CopiesTheLinesItMustCorrect()
    {
        var original = Run(Guid.NewGuid(), out var lineId);

        var amounts = new Dictionary<Guid, PayrollLineAmounts>
        {
            [lineId] = new(6000m, 100m, 50m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 20m, 0m)
        };
        original.MarkCalculated(amounts, new Dictionary<Guid, Guid?> { [lineId] = null },
            new Dictionary<Guid, Guid?> { [lineId] = null }, "accountant");
        original.Approve("hr-manager");

        var adjustment = new PayrollRun("PRL-2026-000011", 2026, 4, "accountant", Guid.NewGuid(), "adjustment");
        adjustment.CopyLineFrom(original.Lines.Single(), "accountant");

        var copied = adjustment.Lines.Single();
        copied.Allowances.Should().Be(100m);
        copied.Incentives.Should().Be(50m);
        copied.EmployeeCode.Should().Be("EMP-001");
        copied.PayrollRunId.Should().Be(adjustment.Id, "the copy belongs to the adjustment run");
    }

    [Fact]
    public void OnlyAPostedRun_CanBeReversed_AndItNeedsAReason()
    {
        var run = Run(Guid.NewGuid(), out _);

        Action beforePost = () => run.Reverse("accountant", "any", Guid.NewGuid());
        beforePost.Should().Throw<DomainException>("only a posted run is reversed");

        run.Approve("hr-manager");
        run.Post("accountant");

        Action noReason = () => run.Reverse("accountant", "   ", Guid.NewGuid());
        noReason.Should().Throw<DomainException>();
    }
}
