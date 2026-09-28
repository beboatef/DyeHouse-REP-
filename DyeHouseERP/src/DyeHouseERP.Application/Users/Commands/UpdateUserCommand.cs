using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Commands;

/// <summary>
/// Updates a login's display name and permission set (spec section 41).
///
/// This deliberately does NOT introduce a Role entity: the existing model is a
/// flat, per-user list of permission names (User.Roles) checked server-side by
/// PermissionAuthorizationHandler, and the request was to keep that model rather
/// than redesign it. What was missing was any way to CHANGE the set after the
/// user was created - that is what this command adds.
///
/// Two safety rules:
///   - permission names are validated against Permissions.All, so a typo can
///     never silently grant nothing (or something unexpected);
///   - an admin cannot change their own permissions, which is how a single-
///     admin installation would otherwise lock itself out permanently.
/// </summary>
public record UpdateUserCommand(Guid UserId, string DisplayName, List<string> Roles) : IRequest<UserDto>;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleForEach(x => x.Roles).Must(BeKnownPermission)
            .WithMessage("'{PropertyValue}' is not a known permission.");
    }

    private static bool BeKnownPermission(string role) =>
        string.Equals(role, Permissions.Admin, StringComparison.OrdinalIgnoreCase) ||
        Permissions.All.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateUserCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (string.Equals(user.Username, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "You cannot change your own permissions - ask another administrator to do it. " +
                "This rule exists so an installation with a single admin cannot lock itself out.");

        // Never allow the last administrator to lose the admin role.
        var currentlyAdmin = user.RoleList.Contains(Permissions.Admin, StringComparer.OrdinalIgnoreCase);
        var willBeAdmin = request.Roles.Contains(Permissions.Admin, StringComparer.OrdinalIgnoreCase);

        if (currentlyAdmin && !willBeAdmin)
        {
            var otherAdmins = await _db.Users
                .Where(u => u.Id != user.Id && u.IsActive && u.Roles.Contains(Permissions.Admin))
                .AnyAsync(cancellationToken);

            if (!otherAdmins)
                throw new DomainException(
                    "This is the only active administrator. Grant the admin permission to another user first.");
        }

        user.SetProfile(request.DisplayName, request.Roles, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = user.RoleList.ToList(),
            IsActive = user.IsActive
        };
    }
}
