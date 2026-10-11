using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

/// <summary>
/// Calculates the lines of a payroll run (spec sections 35-39) from the configured
/// masters: attendance, approved overtime, active advances, the employees' pay
/// component assignments and the statutory contribution rules. Nothing here knows
/// a factory-specific amount - change the master data and the next calculation
/// produces a different payroll.
/// </summary>
public record CalculatePayrollRunCommand(Guid RunId) : IRequest<PayrollRunDto>;

public class CalculatePayrollRunCommandHandler : IRequestHandler<CalculatePayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CalculatePayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(CalculatePayrollRunCommand request, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        if (!run.IsEditable)
            throw new DomainException($"A {run.Status} payroll run can no longer be calculated.");

        var rule = await _db.AttendanceRules.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct)
            ?? throw new DomainException(
                "No attendance policy is active - configure one before calculating payroll.");

        var periodFrom = new DateTime(run.PeriodYear, run.PeriodMonth, 1);
        var periodTo = periodFrom.AddMonths(1).AddDays(-1);

        var employeeIds = run.Lines.Select(l => l.EmployeeId).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var attendance = await _db.AttendanceEvents.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.WorkDate >= periodFrom && a.WorkDate <= periodTo)
            .ToListAsync(ct);

        var overtime = await _db.OvertimeEntries.AsNoTracking()
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.WorkDate >= periodFrom && o.WorkDate <= periodTo)
            .ToListAsync(ct);

        var advances = await _db.EmployeeAdvances.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Status == AdvanceStatus.Active)
            .ToListAsync(ct);

        var assignments = await _db.EmployeePayComponents.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.IsActive)
            .ToListAsync(ct);
        var components = await _db.PayComponents.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        var contributions = await _db.ContributionRules.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);

        // Only PAID leave types count as attendance for a per-day component.
        var paidLeaveTypeIds = await _db.LeaveTypes.AsNoTracking()
            .Where(t => t.IsPaid).Select(t => t.Id).ToListAsync(ct);

        var paidLeaveIds = (await _db.LeaveRequests.AsNoTracking()
                .Where(l => employeeIds.Contains(l.EmployeeId) && l.Status == LeaveStatus.Approved
                            && l.FromDate <= periodTo && l.ToDate >= periodFrom)
                .ToListAsync(ct))
            .Where(l => paidLeaveTypeIds.Contains(l.LeaveTypeId))
            .Select(l => l.Id)
            .ToHashSet();

        var amounts = new Dictionary<Guid, PayrollLineAmounts>();
        var costCenters = new Dictionary<Guid, Guid?>();
        var sections = new Dictionary<Guid, Guid?>();

        foreach (var line in run.Lines)
        {
            if (!employees.TryGetValue(line.EmployeeId, out var employee)) continue;

            var employeeComponents = assignments
                .Where(a => a.EmployeeId == employee.Id)
                .Select(a => (Component: components.GetValueOrDefault(a.PayComponentId), Assignment: a))
                .Where(x => x.Component is not null)
                .Select(x => (x.Component!, x.Assignment))
                .ToList();

            var input = new PayrollCalculationInput(
                employee, rule, periodFrom, periodTo,
                attendance.Where(a => a.EmployeeId == employee.Id).ToList(),
                overtime.Where(o => o.EmployeeId == employee.Id).ToList(),
                advances.Where(a => a.EmployeeId == employee.Id).ToList(),
                employeeComponents,
                contributions,
                paidLeaveIds);

            amounts[line.Id] = PayrollCalculator.Calculate(input);
            costCenters[line.Id] = employee.CostCenterId;
            sections[line.Id] = employee.HrSectionId;
        }

        run.MarkCalculated(amounts, costCenters, sections, _currentUser.UserName);
        await _db.SaveChangesAsync(ct);

        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), ct);
    }
}

/// <summary>Submits a calculated run for approval (spec section 39).</summary>
public record SubmitPayrollRunForReviewCommand(Guid RunId) : IRequest<PayrollRunDto>;

public class SubmitPayrollRunForReviewCommandHandler : IRequestHandler<SubmitPayrollRunForReviewCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public SubmitPayrollRunForReviewCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(SubmitPayrollRunForReviewCommand request, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        run.SubmitForReview(_currentUser.UserName);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), ct);
    }
}

/// <summary>Returns a submitted run to the calculated stage so a line can be corrected (spec section 39).</summary>
public record ReturnPayrollRunForCorrectionCommand(Guid RunId, string? Notes = null) : IRequest<PayrollRunDto>;

public class ReturnPayrollRunForCorrectionCommandHandler
    : IRequestHandler<ReturnPayrollRunForCorrectionCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public ReturnPayrollRunForCorrectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(ReturnPayrollRunForCorrectionCommand request, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        run.ReturnToCalculated(_currentUser.UserName, request.Notes);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), ct);
    }
}

/// <summary>
/// Reverses a POSTED payroll run (spec section 39). Nothing is deleted or edited:
/// an IN treasury row equal-and-opposite to the original OUT is written, the run is
/// marked Reversed with the reason, and a linked ADJUSTMENT draft is opened for the
/// same period carrying the original figures so they can be corrected and re-posted.
/// </summary>
public record ReversePayrollRunCommand(Guid RunId, string Reason) : IRequest<PayrollRunDto>;

public class ReversePayrollRunCommandValidator : AbstractValidator<ReversePayrollRunCommand>
{
    public ReversePayrollRunCommandValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class ReversePayrollRunCommandHandler : IRequestHandler<ReversePayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;
    private readonly ISender _mediator;

    public ReversePayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose, ISender mediator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _periodClose = periodClose; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(ReversePayrollRunCommand request, CancellationToken ct)
    {
        var original = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, ct)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        if (original.Status != PayrollRunStatus.Posted)
            throw new DomainException("Only a posted payroll run can be reversed.");

        // The reversal is dated today, so a closed period only blocks it if today is
        // closed - a correction of an old period stays possible (spec section 43).
        var today = DateTime.UtcNow.Date;
        await _periodClose.EnsureOpenAsync(today, ct);

        var runNumber = await _numberGenerator.NextAsync(DocumentType.PayrollRun, cancellationToken: ct);

        var adjustment = new PayrollRun(runNumber, original.PeriodYear, original.PeriodMonth, _currentUser.UserName,
            original.TreasuryAccountId, $"Adjustment of {original.RunNumber}: {request.Reason.Trim()}");
        adjustment.LinkReversal(original.Id);
        foreach (var line in original.Lines) adjustment.CopyLineFrom(line, _currentUser.UserName);

        _db.PayrollRuns.Add(adjustment);

        var net = original.TotalNet;
        if (net > 0 && original.TreasuryAccountId.HasValue)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                original.TreasuryAccountId.Value, today, DocumentType.PayrollRun,
                $"{original.RunNumber}-REVERSAL", original.Id, net, TreasuryDirection.In,
                $"Reversal of payroll {original.RunNumber}: {request.Reason}", _currentUser.UserName));
        }

        // The reversed run's consumed overtime and recovered advance instalments are
        // handed back, so the linked ADJUSTMENT draft can pay them on its own terms.
        // Without this the correction would find the hours already consumed and the
        // advances already recovered, and would silently under-pay the employees.
        await PayrollConsumption.ReleaseAsync(_db, original, _currentUser.UserName, ct);

        original.Reverse(_currentUser.UserName, request.Reason, adjustment.Id);
        await _db.SaveChangesAsync(ct);

        return await _mediator.Send(new GetPayrollRunByIdQuery(adjustment.Id), ct);
    }
}
