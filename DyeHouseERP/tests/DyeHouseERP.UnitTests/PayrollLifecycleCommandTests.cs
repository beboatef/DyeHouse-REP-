using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// End-to-end wiring of the payroll lifecycle (spec section 39):
/// Draft -> Calculate -> Calculated -> Submit -> UnderReview -> Return -> Calculated
/// -> Approve -> Approved -> Post -> Posted -> Reverse -> Reversed (with a linked adjustment draft).
///
/// These are handler-level tests on purpose. Calculate/Submit/Return/Reverse were implemented in the
/// domain and the handlers but were not reachable from the API/UI; driving the real command handlers
/// against a real DbContext is what catches a regression in that wiring (and in the allowed/forbidden
/// transitions) rather than only in the entity.
/// </summary>
public class PayrollLifecycleCommandTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PayrollLifecycleCommandTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"payroll-lifecycle-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>A real MediatR pipeline so a command can dispatch the module's own query.</summary>
    private static ISender Sender(ApplicationDbContext db, string user = "accountant")
    {
        var current = new StubCurrentUser(user);
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreatePayrollRunCommand>());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(current);
        services.AddSingleton<ICurrentUserService>(current);
        services.AddSingleton<IDateTime>(new FixedClock());
        services.AddSingleton<IPeriodCloseService>(new OpenPeriod());
        services.AddSingleton<IDocumentNumberGenerator>(new FixedNumberGenerator());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    /// <summary>An active policy with a zero absence factor, so the derived amounts are stable.</summary>
    private static AttendanceRule ActiveRule()
    {
        var rule = new AttendanceRule("Standard", "hr");
        rule.Configure(standardDailyHours: 8m, standardWorkingDaysPerMonth: 25m, graceMinutes: 0,
            latePenaltyPerMinute: 0m, earlyLeavePenaltyPerMinute: 0m, absenceDayDeductionFactor: 0m,
            overtimeGraceMinutes: 0, 1.5m, 2m, 2.5m, "5,6", true, true);
        return rule;
    }

    private static async Task<Guid> SeedDraftRunAsync(
        ApplicationDbContext db, bool withActiveRule = true, bool withAccount = false)
    {
        var department = new Department("D-1", "Dyeing", "hr");
        var employee = new Employee("EMP-001", "Ali Hassan", department.Id, 6000m,
            DateTime.UtcNow.Date.AddYears(-1), "hr");
        db.Departments.Add(department);
        db.Employees.Add(employee);
        if (withActiveRule) db.AttendanceRules.Add(ActiveRule());
        await db.SaveChangesAsync();

        Guid? accountId = null;
        if (withAccount)
        {
            var account = new TreasuryAccount("TR-1", "Cash", TreasuryAccountKind.Cash, "hr");
            db.TreasuryAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        var run = new PayrollRun("PRL-2026-000001", 2026, 4, "hr", accountId);
        run.AddLine(employee.Id, employee.Code, employee.Name, department.Id, "Dyeing", employee.BasicSalary, 0m, 0m);
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private static async Task<PayrollRunStatus> StatusAsync(ApplicationDbContext db, Guid runId)
        => (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).Status;

    // ------------------------------------------------------------- Calculate

    [Fact]
    public async Task Calculate_FromDraft_FillsTheAmountsAndMovesToCalculated()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db);

        await Sender(db).Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);

        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.Calculated);
        var run = await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).SingleAsync(r => r.Id == runId);
        run.CalculatedBy.Should().Be("accountant");
        run.TotalGross.Should().BeGreaterThanOrEqualTo(6000m, "the basic salary is always part of the gross");
    }

    [Fact]
    public async Task Calculate_WithNoActiveAttendancePolicy_IsRefused()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withActiveRule: false);

        var act = () => Sender(db).Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.Draft, "a refused calculation changes nothing");
    }

    [Fact]
    public async Task Calculate_OnAnApprovedRun_IsRefused()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);
        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);

        var act = () => sender.Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>("an approved run is no longer editable");
    }

    // ---------------------------------------------------------------- Submit

    [Fact]
    public async Task Submit_OnlyFromCalculated()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db);
        var sender = Sender(db);

        var fromDraft = () => sender.Send(new SubmitPayrollRunForReviewCommand(runId), CancellationToken.None);
        await fromDraft.Should().ThrowAsync<DomainException>("a draft has not been calculated yet");

        await sender.Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new SubmitPayrollRunForReviewCommand(runId), CancellationToken.None);
        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.UnderReview);

        var again = () => sender.Send(new SubmitPayrollRunForReviewCommand(runId), CancellationToken.None);
        await again.Should().ThrowAsync<DomainException>("an under-review run cannot be re-submitted");
    }

    // ---------------------------------------------------------------- Return

    [Fact]
    public async Task Return_OnlyFromUnderReview_AndItRecordsTheNote()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db);
        var sender = Sender(db);

        var fromCalculated = () => sender.Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);
        await fromCalculated.Should().NotThrowAsync();

        var earlyReturn = () => sender.Send(new ReturnPayrollRunForCorrectionCommand(runId, "n/a"), CancellationToken.None);
        await earlyReturn.Should().ThrowAsync<DomainException>("only an under-review run can be returned");

        await sender.Send(new SubmitPayrollRunForReviewCommand(runId), CancellationToken.None);
        await sender.Send(new ReturnPayrollRunForCorrectionCommand(runId, "recheck attendance"), CancellationToken.None);

        var run = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.Calculated);
        run.ReviewNotes.Should().Be("recheck attendance");
        run.ReviewedBy.Should().Be("accountant");
    }

    // --------------------------------------------------------------- Approve

    [Fact]
    public async Task Approve_IsAllowedFromCalculated_ButNotAfterPosting()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);

        await sender.Send(new CalculatePayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);
        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.Approved);

        await sender.Send(new PostPayrollRunCommand(runId), CancellationToken.None);
        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.Posted);

        var reApprove = () => sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);
        await reApprove.Should().ThrowAsync<DomainException>("a posted run is never re-approved");
    }

    // --------------------------------------------------------------- Reverse

    [Fact]
    public async Task Reverse_OnPostedRun_PostsTheOppositeTreasuryRowAndOpensAnAdjustmentDraft()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);

        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new PostPayrollRunCommand(runId), CancellationToken.None);
        var posted = await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).SingleAsync(r => r.Id == runId);

        var adjustment = await sender.Send(new ReversePayrollRunCommand(runId, "wrong salary applied"), CancellationToken.None);

        // The original is reversed and points at the adjustment; nothing was deleted.
        var original = await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).SingleAsync(r => r.Id == runId);
        original.Status.Should().Be(PayrollRunStatus.Reversed);
        original.ReversalReason.Should().Be("wrong salary applied");
        original.ReversedByPayrollRunId.Should().Be(adjustment.Id);
        original.TotalNet.Should().Be(posted.TotalNet, "the posted figure is kept for the reversal to reference");

        // A linked ADJUSTMENT draft carries the original figures for correction.
        var draft = await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).SingleAsync(r => r.Id == adjustment.Id);
        draft.Status.Should().Be(PayrollRunStatus.Draft);
        draft.ReversesPayrollRunId.Should().Be(runId);
        draft.Lines.Should().HaveCount(1);
        draft.Lines.Single().BasicSalary.Should().Be(6000m);
        adjustment.Status.Should().Be(PayrollRunStatus.Draft, "the caller is handed the draft it must now correct");

        // The treasury gets the equal-and-opposite IN row - the OUT row is never touched.
        var treasury = await db.TreasuryTransactions.AsNoTracking().Where(t => t.SourceDocumentId == runId).ToListAsync();
        treasury.Should().ContainSingle(t => t.Direction == TreasuryDirection.Out && t.Amount == posted.TotalNet);
        treasury.Should().ContainSingle(t => t.Direction == TreasuryDirection.In && t.Amount == posted.TotalNet);
    }

    [Fact]
    public async Task Reverse_OnANonPostedRun_IsRefused()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);

        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);

        var act = () => sender.Send(new ReversePayrollRunCommand(runId, "not yet paid"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>("only a posted run is reversed");
        (await StatusAsync(db, runId)).Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task Reverse_IsNeverReplayed_Twice()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);

        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new PostPayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new ReversePayrollRunCommand(runId, "first correction"), CancellationToken.None);

        var again = () => sender.Send(new ReversePayrollRunCommand(runId, "second correction"), CancellationToken.None);

        await again.Should().ThrowAsync<DomainException>("a reversed run is never reversed again");
        (await db.PayrollRuns.AsNoTracking().CountAsync(r => r.ReversesPayrollRunId == runId)).Should().Be(1,
            "exactly one adjustment draft exists for the original");
    }

    [Fact]
    public async Task Cancel_AfterReversal_IsRefused()
    {
        using var db = NewDb();
        var runId = await SeedDraftRunAsync(db, withAccount: true);
        var sender = Sender(db);

        await sender.Send(new ApprovePayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new PostPayrollRunCommand(runId), CancellationToken.None);
        await sender.Send(new ReversePayrollRunCommand(runId, "reverse"), CancellationToken.None);

        var act = () => sender.Send(new CancelPayrollRunCommand(runId, "cancel instead"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>("the reversal is the correction, not a cancellation");
    }

    [Fact]
    public void Reverse_Validator_RequiresAReason()
    {
        var validator = new ReversePayrollRunCommandValidator();

        validator.Validate(new ReversePayrollRunCommand(Guid.NewGuid(), "")).IsValid.Should().BeFalse();
        validator.Validate(new ReversePayrollRunCommand(Guid.Empty, "reason")).IsValid.Should().BeFalse();
        validator.Validate(new ReversePayrollRunCommand(Guid.NewGuid(), "wrong salary")).IsValid.Should().BeTrue();
    }
}
