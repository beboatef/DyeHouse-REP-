using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DyeHouseERP.Application.Users.Commands;

/// <summary>
/// Administrator reset of another login's password (Part 2). The reset password is never the
/// user's own choice, so the account is flagged must-change: the user picks their own at the
/// next sign-in. Every reset revokes all refresh tokens (reason "PasswordChanged") and writes
/// an audit entry. The endpoint is gated on users.reset_password (see UsersController).
/// </summary>
public record ChangePasswordCommand(Guid UserId, string NewPassword) : IRequest<Unit>;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
        // Length only - the username and history rules need the database and run in the handler.
        => RuleFor(x => x.NewPassword).NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength).WithMessage(PasswordPolicy.TooShortMessage)
            .MaximumLength(PasswordPolicy.MaxLength).WithMessage(PasswordPolicy.TooLongMessage);
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher,
        ILogger<ChangePasswordCommandHandler>? logger = null)
    {
        _db = db; _currentUser = currentUser; _passwordHasher = passwordHasher;
        _logger = logger ?? NullLogger<ChangePasswordCommandHandler>.Instance;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // Server-side: resetting a password is modifying the account, so an administrator account
        // can only be reset by an administrator - otherwise users.reset_password would be a route to
        // taking over an admin login instead of a way to help an ordinary user back in.
        UserAdministrationGuard.EnsureCanModifyAccount(_currentUser, user);

        await PasswordChangeService.ApplyAsync(
            _db, user, request.NewPassword, _currentUser.UserName, SafeIp(),
            userInitiated: false, _passwordHasher, _logger, cancellationToken);
        return Unit.Value;
    }

    private string? SafeIp()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }
}
