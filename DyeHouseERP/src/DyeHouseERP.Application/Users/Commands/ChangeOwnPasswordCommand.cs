using DyeHouseERP.Application.Auth.Commands;
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
/// The signed-in user changes their OWN password (Part 2). This is the endpoint the
/// must-change gate leaves open: it proves knowledge of the current password, enforces the
/// full policy, clears the flag, revokes every other session (reason "PasswordChanged") and
/// writes an audit entry. No permission required beyond being authenticated - a flagged user
/// holds no useful permission until this succeeds.
/// </summary>
public record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;

public class ChangeOwnPasswordCommandValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);
        // Length only - the username and history rules need the database and run in the handler.
        RuleFor(x => x.NewPassword).NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength).WithMessage(PasswordPolicy.TooShortMessage)
            .MaximumLength(PasswordPolicy.MaxLength).WithMessage(PasswordPolicy.TooLongMessage);
    }
}

public class ChangeOwnPasswordCommandHandler : IRequestHandler<ChangeOwnPasswordCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ChangeOwnPasswordCommandHandler> _logger;

    public ChangeOwnPasswordCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher,
        ILogger<ChangeOwnPasswordCommandHandler>? logger = null)
    {
        _db = db; _currentUser = currentUser; _passwordHasher = passwordHasher;
        _logger = logger ?? NullLogger<ChangeOwnPasswordCommandHandler>.Instance;
    }

    public async Task<Unit> Handle(ChangeOwnPasswordCommand request, CancellationToken cancellationToken)
    {
        // The identity comes from the JWT, never from the body: a caller can only ever change
        // their own password through this endpoint, however the request is forged.
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == _currentUser.UserName && u.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("You do not have permission to perform this action.");

        // The current password must be proven even in a live session: a walked-away unlocked
        // terminal must not be enough to take over the account permanently.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new InvalidCredentialsException();

        await PasswordChangeService.ApplyAsync(
            _db, user, request.NewPassword, _currentUser.UserName, SafeIp(),
            userInitiated: true, _passwordHasher, _logger, cancellationToken);
        return Unit.Value;
    }

    private string? SafeIp()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }
}
