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
/// Creates a monthly payroll run (spec section 36) and builds one line per
/// currently-active employee from their basic salary. The run starts as a draft:
/// nothing is paid until it is approved and posted.
/// </summary>
public record CreatePayrollRunCommand(
    int PeriodYear,
    int PeriodMonth,
    Guid? TreasuryAccountId = null,
    string? Notes = null,
    bool IncludeAllActiveEmployees = true,
    List<Guid>? EmployeeIds = null) : IRequest<PayrollRunDto>;

public class CreatePayrollRunCommandValidator : AbstractValidator<CreatePayrollRunCommand>
{
    public CreatePayrollRunCommandValidator()
    {
        RuleFor(x => x.PeriodMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.PeriodYear).InclusiveBetween(2000, 2200);
    }
}

public class CreatePayrollRunCommandHandler : IRequestHandler<CreatePayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly ISender _mediator;

    public CreatePayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, ISender mediator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(CreatePayrollRunCommand request, CancellationToken cancellationToken)
    {
        // One run per period - a month is never paid twice (spec section 53).
        var existing = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(
            r => r.PeriodYear == request.PeriodYear && r.PeriodMonth == request.PeriodMonth
                 && r.Status != PayrollRunStatus.Cancelled, cancellationToken);
        if (existing is not null)
            throw new DomainException(
                $"A payroll run for {request.PeriodMonth:D2}/{request.PeriodYear} already exists ({existing.RunNumber}).");

        if (request.TreasuryAccountId.HasValue &&
            !await _db.TreasuryAccounts.AnyAsync(a => a.Id == request.TreasuryAccountId.Value, cancellationToken))
            throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId.Value);

        var employeesQuery = _db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);

        if (request.EmployeeIds is { Count: > 0 })
            employeesQuery = employeesQuery.Where(e => request.EmployeeIds.Contains(e.Id));
        else if (!request.IncludeAllActiveEmployees)
            employeesQuery = employeesQuery.Where(e => false);

        var employees = await employeesQuery.OrderBy(e => e.Code).ToListAsync(cancellationToken);
        if (employees.Count == 0)
            throw new DomainException("There are no active employees to include in this payroll run.");

        var runNumber = await _numberGenerator.NextAsync(DocumentType.PayrollRun, cancellationToken: cancellationToken);

        var run = new PayrollRun(runNumber, request.PeriodYear, request.PeriodMonth, _currentUser.UserName,
            request.TreasuryAccountId, request.Notes);

        var departments = await PayrollDtoBuilder.DepartmentLookupAsync(
            _db, employees.Select(e => e.DepartmentId), cancellationToken);

        foreach (var employee in employees)
        {
            run.AddLine(
                employee.Id, employee.Code, employee.Name, employee.DepartmentId,
                departments.TryGetValue(employee.DepartmentId, out var d) ? d.Name : null,
                employee.BasicSalary, allowances: 0m, deductions: 0m);
        }

        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

public record UpdatePayrollRunLineCommand(
    Guid RunId, Guid LineId, decimal Allowances, decimal Deductions, string? Notes = null) : IRequest<PayrollRunDto>;

public class UpdatePayrollRunLineCommandHandler : IRequestHandler<UpdatePayrollRunLineCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public UpdatePayrollRunLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(UpdatePayrollRunLineCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        run.UpdateLineAmounts(request.LineId, request.Allowances, request.Deductions, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

public record RemovePayrollRunLineCommand(Guid RunId, Guid LineId) : IRequest<PayrollRunDto>;

public class RemovePayrollRunLineCommandHandler : IRequestHandler<RemovePayrollRunLineCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public RemovePayrollRunLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(RemovePayrollRunLineCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        run.RemoveLine(request.LineId, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

public record SetPayrollRunAccountCommand(Guid RunId, Guid? TreasuryAccountId) : IRequest<PayrollRunDto>;

public class SetPayrollRunAccountCommandHandler : IRequestHandler<SetPayrollRunAccountCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public SetPayrollRunAccountCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(SetPayrollRunAccountCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.RunId);

        if (request.TreasuryAccountId.HasValue &&
            !await _db.TreasuryAccounts.AnyAsync(a => a.Id == request.TreasuryAccountId.Value, cancellationToken))
            throw new NotFoundException("TreasuryAccount", request.TreasuryAccountId.Value);

        run.SetTreasuryAccount(request.TreasuryAccountId, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

public record ApprovePayrollRunCommand(Guid Id) : IRequest<PayrollRunDto>;

public class ApprovePayrollRunCommandHandler : IRequestHandler<ApprovePayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public ApprovePayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(ApprovePayrollRunCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.Id);

        if (!run.TreasuryAccountId.HasValue)
            throw new DomainException("Select the bank/cash account this payroll will be paid from before approving.");

        run.Approve(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

/// <summary>
/// Posts the payroll run: the net total leaves the selected treasury/bank account.
/// This is the only step with a financial effect (spec sections 36, 41 and 46).
/// </summary>
public record PostPayrollRunCommand(Guid Id) : IRequest<PayrollRunDto>;

public class PostPayrollRunCommandHandler : IRequestHandler<PostPayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodCloseService _periodClose;
    private readonly ISender _mediator;

    public PostPayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IPeriodCloseService periodClose, ISender mediator)
    { _db = db; _currentUser = currentUser; _periodClose = periodClose; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(PostPayrollRunCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.Id);

        var postingDate = new DateTime(run.PeriodYear, run.PeriodMonth,
            DateTime.DaysInMonth(run.PeriodYear, run.PeriodMonth));
        await _periodClose.EnsureOpenAsync(postingDate, cancellationToken);

        run.Post(_currentUser.UserName);

        var net = run.TotalNet;
        if (net > 0)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                run.TreasuryAccountId!.Value, postingDate, DocumentType.PayrollRun, run.RunNumber, run.Id,
                net, TreasuryDirection.Out,
                $"Payroll {run.PeriodMonth:D2}/{run.PeriodYear} for {run.Lines.Count} employee(s)",
                _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}

/// <summary>
/// Cancels a payroll run. If it was already posted, the equal-and-opposite IN
/// treasury row is written - nothing is ever deleted (spec sections 46 and 53).
/// </summary>
public record CancelPayrollRunCommand(Guid Id, string Reason) : IRequest<PayrollRunDto>;

public class CancelPayrollRunCommandValidator : AbstractValidator<CancelPayrollRunCommand>
{
    public CancelPayrollRunCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CancelPayrollRunCommandHandler : IRequestHandler<CancelPayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CancelPayrollRunCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PayrollRunDto> Handle(CancelPayrollRunCommand request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.Id);

        if (run.Status == PayrollRunStatus.Cancelled)
            throw new DomainException("This payroll run is already cancelled.");

        var wasPosted = run.Status == PayrollRunStatus.Posted;
        var net = run.TotalNet;
        var accountId = run.TreasuryAccountId;

        run.Cancel(_currentUser.UserName, request.Reason);

        if (wasPosted && net > 0 && accountId.HasValue)
        {
            _db.TreasuryTransactions.Add(new TreasuryTransaction(
                accountId.Value, DateTime.UtcNow.Date, DocumentType.PayrollRun,
                $"{run.RunNumber}-CANCEL", run.Id, net, TreasuryDirection.In,
                $"Cancellation of payroll {run.RunNumber}: {request.Reason}",
                _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPayrollRunByIdQuery(run.Id), cancellationToken);
    }
}
