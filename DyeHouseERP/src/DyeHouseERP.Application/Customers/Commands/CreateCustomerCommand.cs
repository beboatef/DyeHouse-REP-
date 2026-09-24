using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Commands;

public record CreateCustomerCommand(string Code, string Name) : IRequest<CustomerDto>;

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
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
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);

        return new CustomerDto { Id = customer.Id, Code = customer.Code, Name = customer.Name, IsActive = customer.IsActive };
    }
}
