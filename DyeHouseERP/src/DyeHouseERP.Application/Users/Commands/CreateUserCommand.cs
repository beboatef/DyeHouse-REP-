using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Commands;

/// <summary>Creates a login (spec: role-based access such as inventory.allow_negative_stock / production.approve_stage). Roles are passed as a plain list and stored comma-separated - see User.Roles.</summary>
public record CreateUserCommand(string Username, string Password, string DisplayName, List<string> Roles) : IRequest<UserDto>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher)
    {
        _db = db; _currentUser = currentUser; _passwordHasher = passwordHasher;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.Username, cancellationToken))
            throw new DuplicateCodeException("User", request.Username);

        var user = new User(
            request.Username, _passwordHasher.Hash(request.Password), request.DisplayName,
            string.Join(",", request.Roles), _currentUser.UserName);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return new UserDto { Id = user.Id, Username = user.Username, DisplayName = user.DisplayName, Roles = user.RoleList.ToList(), IsActive = user.IsActive };
    }
}
