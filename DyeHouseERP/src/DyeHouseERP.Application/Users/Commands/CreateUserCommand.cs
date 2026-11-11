using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Commands;

/// <summary>
/// Creates a login (spec: role-based access such as inventory.allow_negative_stock). Roles are passed as a
/// plain list and stored comma-separated - see User.Roles.
///
/// Permission names are validated against <see cref="Permissions.All"/> on create exactly as they are on
/// update, so a typo can never silently store a right that does not exist (and therefore grants nothing).
/// </summary>
public record CreateUserCommand(string Username, string Password, string DisplayName, List<string> Roles) : IRequest<UserDto>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        // Part 2: floor raised 8 -> 12 (matches the bootstrap minimum); the username and
        // history rules need the database, so they run in the handler via PasswordPolicy.
        // R5 ceiling kept: an unbounded password cannot bloat the user row or hashing cost.
        RuleFor(x => x.Password).NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength).WithMessage(PasswordPolicy.TooShortMessage)
            .MaximumLength(PasswordPolicy.MaxLength).WithMessage(PasswordPolicy.TooLongMessage);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleForEach(x => x.Roles).Must(BeKnownPermission)
            .WithMessage("'{PropertyValue}' is not a known permission.");
    }

    private static bool BeKnownPermission(string role) =>
        string.Equals(role, Permissions.Admin, StringComparison.OrdinalIgnoreCase) ||
        Permissions.All.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher,
        ILogger<CreateUserCommandHandler>? logger = null)
    {
        _db = db; _currentUser = currentUser; _passwordHasher = passwordHasher;
        _logger = logger ?? NullLogger<CreateUserCommandHandler>.Instance;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Server-side: the rights written onto the new login may never exceed the caller's own.
        // A users.manage holder can therefore create ordinary logins, but cannot mint an
        // administrator (or any other right they do not hold) through this endpoint.
        UserAdministrationGuard.EnsureCanGrantPermissions(_currentUser, request.Roles);

        if (await _db.Users.AnyAsync(u => u.Username == request.Username, cancellationToken))
            throw new DuplicateCodeException("User", request.Username);

        var user = new User(
            request.Username, _passwordHasher.Hash(request.Password), request.DisplayName,
            string.Join(",", request.Roles), _currentUser.UserName);

        _db.Users.Add(user);

        // Part 2: an administrator-created password is never the user's own choice - the
        // account starts flagged, the password lands in history, and no session survives
        // (there are no tokens yet, so revocation is a no-op, but the audit row matters).
        // The username/history part of the policy runs here (the validator only checks length).
        // The live hash is passed as null: it IS the new password's own hash, and comparing
        // a password against itself would refuse every creation.
        PasswordPolicy.EnsureValid(
            nameof(request.Password), user.Username, request.Password,
            null, Enumerable.Empty<string>(), _passwordHasher);
        user.MarkPasswordSetByAdministrator(_currentUser.UserName);
        _db.PasswordHistories.Add(new PasswordHistory(user.Id, user.PasswordHash, _currentUser.UserName));
        _db.AuditLogEntries.Add(new AuditLogEntry(
            DateTime.UtcNow, _currentUser.UserName, "UserCreated", nameof(User),
            user.Id.ToString(), null, null, SafeIp(),
            $"Administrator created the login '{user.Username}' with a forced password change."));
        await _db.SaveChangesAsync(cancellationToken);

        return new UserDto { Id = user.Id, Username = user.Username, DisplayName = user.DisplayName, Roles = user.RoleList.ToList(), IsActive = user.IsActive, MustChangePassword = user.MustChangePassword };
    }

    private string? SafeIp()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }
}
