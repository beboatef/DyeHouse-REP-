using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Payroll.DTOs;
using DyeHouseERP.Application.Payroll.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Payroll.Commands;

// ------------------------------------------------------------ departments

/// <summary>Creates a department (spec section 36). Code is manually entered and unique.</summary>
public record CreateDepartmentCommand(
    string Code, string? NameAr = null, string? NameEn = null, string? Notes = null) : IRequest<DepartmentDto>;

public class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A department name is required (Arabic and/or English).");
    }
}

public class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, DepartmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CreateDepartmentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<DepartmentDto> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await _db.Departments.AnyAsync(d => d.Code == code, cancellationToken))
            throw new DuplicateCodeException("Department", code);

        var department = new Department(code, request.NameEn ?? request.NameAr ?? code,
            _currentUser.UserName, request.NameAr, request.NameEn, request.Notes);

        _db.Departments.Add(department);
        await _db.SaveChangesAsync(cancellationToken);

        return new DepartmentDto
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            NameAr = department.NameAr,
            NameEn = department.NameEn,
            Notes = department.Notes,
            IsActive = department.IsActive,
            EmployeeCount = 0
        };
    }
}

public record UpdateDepartmentCommand(
    Guid Id, string? NameAr = null, string? NameEn = null, string? Notes = null, bool? IsActive = null)
    : IRequest<DepartmentDto>;

public class UpdateDepartmentCommandHandler : IRequestHandler<UpdateDepartmentCommand, DepartmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateDepartmentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<DepartmentDto> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Department", request.Id);

        if (!string.IsNullOrWhiteSpace(request.NameAr) || !string.IsNullOrWhiteSpace(request.NameEn))
            department.SetNames(request.NameAr, request.NameEn);

        if (request.Notes is not null) department.SetNotes(request.Notes);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) department.Activate(_currentUser.UserName);
            else department.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var activeEmployees = await _db.Employees.AsNoTracking()
            .CountAsync(e => e.DepartmentId == department.Id && e.Status == Domain.Enums.EmployeeStatus.Active, cancellationToken);

        return new DepartmentDto
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            NameAr = department.NameAr,
            NameEn = department.NameEn,
            Notes = department.Notes,
            IsActive = department.IsActive,
            EmployeeCount = activeEmployees
        };
    }
}

// -------------------------------------------------------------- employees

/// <summary>Creates an employee (spec section 36). Payroll is independent of job-order costing.</summary>
public record CreateEmployeeCommand(
    string Code,
    Guid DepartmentId,
    decimal BasicSalary,
    DateTime HireDate,
    string? NameAr = null,
    string? NameEn = null,
    string? JobTitle = null,
    string? Phone = null,
    string? NationalId = null,
    string? BankAccountNumber = null,
    string? Notes = null) : IRequest<EmployeeDto>;

public class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.BasicSalary).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("An employee name is required (Arabic and/or English).");
    }
}

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, EmployeeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CreateEmployeeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await _db.Employees.AnyAsync(e => e.Code == code, cancellationToken))
            throw new DuplicateCodeException("Employee", code);

        if (!await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId, cancellationToken))
            throw new NotFoundException("Department", request.DepartmentId);

        var employee = new Employee(code, request.NameEn ?? request.NameAr ?? code, request.DepartmentId,
            request.BasicSalary, request.HireDate, _currentUser.UserName,
            request.NameAr, request.NameEn, request.JobTitle, request.Phone,
            request.NationalId, request.BankAccountNumber, request.Notes);

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetEmployeeByIdQuery(employee.Id), cancellationToken);
    }
}

public record UpdateEmployeeCommand(
    Guid Id,
    Guid? DepartmentId = null,
    decimal? BasicSalary = null,
    DateTime? HireDate = null,
    string? NameAr = null,
    string? NameEn = null,
    string? JobTitle = null,
    string? Phone = null,
    string? NationalId = null,
    string? BankAccountNumber = null,
    string? Notes = null,
    string? Status = null) : IRequest<EmployeeDto>;

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, EmployeeDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public UpdateEmployeeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<EmployeeDto> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Employee", request.Id);

        if (!string.IsNullOrWhiteSpace(request.NameAr) || !string.IsNullOrWhiteSpace(request.NameEn))
            employee.SetNames(request.NameAr, request.NameEn);

        if (request.DepartmentId.HasValue || request.BasicSalary.HasValue)
        {
            var departmentId = request.DepartmentId ?? employee.DepartmentId;
            var basicSalary = request.BasicSalary ?? employee.BasicSalary;

            if (!await _db.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken))
                throw new NotFoundException("Department", departmentId);

            employee.SetSalaryAndDepartment(departmentId, basicSalary);
        }

        if (!string.IsNullOrWhiteSpace(request.JobTitle)) employee.SetJobTitle(request.JobTitle);

        employee.SetHrDetails(
            request.Phone ?? employee.Phone,
            request.NationalId ?? employee.NationalId,
            request.BankAccountNumber ?? employee.BankAccountNumber,
            request.Notes ?? employee.Notes);

        if (!string.IsNullOrWhiteSpace(request.Status) && !request.HireDate.HasValue)
        {
            // Status transitions are explicit and audited (spec section 46).
            switch (request.Status.Trim().ToLowerInvariant())
            {
                case "active": employee.Reactivate(_currentUser.UserName); break;
                case "suspended": employee.Suspend(_currentUser.UserName); break;
                case "terminated": employee.Terminate(DateTime.UtcNow.Date, _currentUser.UserName); break;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetEmployeeByIdQuery(employee.Id), cancellationToken);
    }
}
