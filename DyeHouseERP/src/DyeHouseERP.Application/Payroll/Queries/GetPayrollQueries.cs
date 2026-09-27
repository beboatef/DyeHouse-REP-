using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Queries;

// ------------------------------------------------------------ departments

/// <summary>Departments (spec section 36). Configurable data, never a hard-coded list.</summary>
public record GetDepartmentsQuery(bool? ActiveOnly = null, string? Search = null) : IRequest<List<DepartmentDto>>;

public class GetDepartmentsQueryHandler : IRequestHandler<GetDepartmentsQuery, List<DepartmentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetDepartmentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<DepartmentDto>> Handle(GetDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Departments.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true) query = query.Where(d => d.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(d => d.Code.Contains(term) || d.Name.Contains(term) ||
                d.NameAr.Contains(term) || d.NameEn.Contains(term));
        }

        var departments = await query.OrderBy(d => d.Code).ToListAsync(cancellationToken);

        var counts = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active)
            .GroupBy(e => e.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, cancellationToken);

        return departments.Select(d => new DepartmentDto
        {
            Id = d.Id,
            Code = d.Code,
            Name = d.Name,
            NameAr = d.NameAr,
            NameEn = d.NameEn,
            Notes = d.Notes,
            IsActive = d.IsActive,
            EmployeeCount = counts.GetValueOrDefault(d.Id)
        }).ToList();
    }
}

// -------------------------------------------------------------- employees

/// <summary>Employee master list (spec section 36), filterable by department and status.</summary>
public record GetEmployeesQuery(
    Guid? DepartmentId = null,
    EmployeeStatus? Status = null,
    bool? ActiveOnly = null,
    string? Search = null) : IRequest<List<EmployeeDto>>;

public class GetEmployeesQueryHandler : IRequestHandler<GetEmployeesQuery, List<EmployeeDto>>
{
    private readonly IApplicationDbContext _db;
    public GetEmployeesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<EmployeeDto>> Handle(GetEmployeesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Employees.AsNoTracking().AsQueryable();

        if (request.DepartmentId.HasValue) query = query.Where(e => e.DepartmentId == request.DepartmentId);
        if (request.Status.HasValue) query = query.Where(e => e.Status == request.Status);
        if (request.ActiveOnly == true) query = query.Where(e => e.Status == EmployeeStatus.Active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(e => e.Code.Contains(term) || e.Name.Contains(term) ||
                e.NameAr.Contains(term) || e.NameEn.Contains(term) ||
                (e.JobTitle != null && e.JobTitle.Contains(term)));
        }

        var employees = await query.OrderBy(e => e.Code).ToListAsync(cancellationToken);
        var departments = await PayrollDtoBuilder.DepartmentLookupAsync(_db, employees.Select(e => e.DepartmentId), cancellationToken);

        return employees.Select(e => PayrollDtoBuilder.MapEmployee(e, departments)).ToList();
    }
}

public record GetEmployeeByIdQuery(Guid Id) : IRequest<EmployeeDto>;

public class GetEmployeeByIdQueryHandler : IRequestHandler<GetEmployeeByIdQuery, EmployeeDto>
{
    private readonly IApplicationDbContext _db;
    public GetEmployeeByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<EmployeeDto> Handle(GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Employee", request.Id);

        var departments = await PayrollDtoBuilder.DepartmentLookupAsync(_db, new[] { employee.DepartmentId }, cancellationToken);
        return PayrollDtoBuilder.MapEmployee(employee, departments);
    }
}

// ------------------------------------------------------------ payroll runs

/// <summary>Payroll runs (spec section 36), filterable by period and status.</summary>
public record GetPayrollRunsQuery(int? PeriodYear = null, PayrollRunStatus? Status = null)
    : IRequest<List<PayrollRunDto>>;

public class GetPayrollRunsQueryHandler : IRequestHandler<GetPayrollRunsQuery, List<PayrollRunDto>>
{
    private readonly IApplicationDbContext _db;
    public GetPayrollRunsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<PayrollRunDto>> Handle(GetPayrollRunsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.PayrollRuns.AsNoTracking().Include(r => r.Lines).AsQueryable();

        if (request.PeriodYear.HasValue) query = query.Where(r => r.PeriodYear == request.PeriodYear);
        if (request.Status.HasValue) query = query.Where(r => r.Status == request.Status);

        var runs = await query
            .OrderByDescending(r => r.PeriodYear)
            .ThenByDescending(r => r.PeriodMonth)
            .ToListAsync(cancellationToken);

        var accounts = await PayrollDtoBuilder.TreasuryLookupAsync(_db, runs.Where(r => r.TreasuryAccountId.HasValue).Select(r => r.TreasuryAccountId!.Value), cancellationToken);

        return runs.Select(r => PayrollDtoBuilder.MapRun(r, accounts)).ToList();
    }
}

public record GetPayrollRunByIdQuery(Guid Id) : IRequest<PayrollRunDto>;

public class GetPayrollRunByIdQueryHandler : IRequestHandler<GetPayrollRunByIdQuery, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    public GetPayrollRunByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PayrollRunDto> Handle(GetPayrollRunByIdQuery request, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PayrollRun", request.Id);

        var accounts = await PayrollDtoBuilder.TreasuryLookupAsync(
            _db, run.TreasuryAccountId.HasValue ? new[] { run.TreasuryAccountId.Value } : Array.Empty<Guid>(), cancellationToken);

        return PayrollDtoBuilder.MapRun(run, accounts);
    }
}
