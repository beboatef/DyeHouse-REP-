using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Commands;

public record CreateCustomerCommand(
    string Code,
    string Name,
    string? Phone = null,
    string? Address = null,
    string? ContactPerson = null,
    string? TaxNumber = null) : IRequest<CustomerDto>;

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
        RuleFor(x => x.TaxNumber).MaximumLength(50);
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateCustomerCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var exists = await _db.Customers.AnyAsync(c => c.Code == request.Code, cancellationToken);
        if (exists)
            throw new DuplicateCodeException("Customer", request.Code);

        var customer = new Customer(request.Code, request.Name, _currentUser.UserName);
        customer.SetContact(request.Phone, request.Address, request.ContactPerson, request.TaxNumber, _currentUser.UserName);

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);

        return CustomerMapper.ToDto(customer);
    }
}

/// <summary>
/// Updates a customer. The Code is deliberately NOT updatable: it is the join key every
/// downstream document already references, so renaming it would orphan history. Deactivation is
/// a separate explicit action, never a side effect of an edit.
/// </summary>
public record UpdateCustomerCommand(
    Guid Id, string Name,
    string? Phone = null, string? Address = null,
    string? ContactPerson = null, string? TaxNumber = null) : IRequest<CustomerDto>;

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
        RuleFor(x => x.TaxNumber).MaximumLength(50);
    }
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, CustomerDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateCustomerCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerDto> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Customer", request.Id);

        customer.SetName(request.Name, _currentUser.UserName);
        customer.SetContact(request.Phone, request.Address, request.ContactPerson, request.TaxNumber, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CustomerMapper.ToDto(customer);
    }
}

/// <summary>
/// Deactivation instead of deletion: a customer that owns raw material, invoices and deliveries
/// must stay resolvable forever, so the record is only ever switched off.
/// </summary>
public record SetCustomerActiveCommand(Guid Id, bool IsActive) : IRequest<CustomerDto>;

public class SetCustomerActiveCommandHandler : IRequestHandler<SetCustomerActiveCommand, CustomerDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CustomerDto> Handle(SetCustomerActiveCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Customer", request.Id);

        if (request.IsActive) customer.Activate(_currentUser.UserName);
        else customer.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CustomerMapper.ToDto(customer);
    }
}

internal static class CustomerMapper
{
    public static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        IsActive = c.IsActive,
        Phone = c.Phone,
        Address = c.Address,
        ContactPerson = c.ContactPerson,
        TaxNumber = c.TaxNumber
    };
}