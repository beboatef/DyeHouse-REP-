using DyeHouseERP.Application.Auth;
using DyeHouseERP.Application.Auth.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Auth.Commands;

public record LoginCommand(string Username, string Password) : IRequest<LoginResultDto>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>
    /// M8: hard length ceilings on the login payload. Without them a caller can
    /// post megabytes of "username", which bloats the audit row written on every
    /// failure and turns the login endpoint into a cheap memory/IO amplifier.
    /// </summary>
    public const int MaxUsernameLength = 100;
    public const int MaxPasswordLength = 200;

    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(MaxUsernameLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(MaxPasswordLength);
    }
}

/// <summary>Throws a plain exception (mapped to 401 by the API) rather than leaking whether the username or password was wrong.</summary>
public class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid username or password.") { }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUserService _currentUser;

    public LoginCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher, ITokenService tokenService, ICurrentUserService currentUser)
    {
        _db = db; _passwordHasher = passwordHasher; _tokenService = tokenService; _currentUser = currentUser;
    }

    public async Task<LoginResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive, cancellationToken);

        // Exactly one PBKDF2 verification runs either way. Short-circuiting on a missing user made
        // an unknown username answer measurably faster than a wrong password, which told an
        // attacker which accounts exist before they had guessed anything.
        var passwordMatches = _passwordHasher.VerifyOrDecoy(request.Password, user?.PasswordHash);

        if (user is null || !passwordMatches)
        {
            // A failed attempt is itself an auditable security event (spec section 42
            // "Login" coverage) - recorded even though no LoginResultDto is returned.
            _db.AuditLogEntries.Add(new AuditLogEntry(
                DateTime.UtcNow, request.Username, "LoginFailed", nameof(User), null, null, null, SafeIp(), null));
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        var roles = user.RoleList.ToList();
        var token = _tokenService.GenerateToken(user.Id, user.Username, roles, out var expiresAtUtc);

        // A fresh opaque refresh token is minted with every successful login. Only its SHA-256 hash
        // is stored, so a database read never yields a usable session.
        var refreshToken = RefreshTokenCodec.NewToken();
        var refreshExpiresAtUtc = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime);
        _db.UserRefreshTokens.Add(new UserRefreshToken(user.Id, RefreshTokenCodec.Hash(refreshToken), refreshExpiresAtUtc));

        _db.AuditLogEntries.Add(new AuditLogEntry(
            DateTime.UtcNow, user.Username, "Login", nameof(User), user.Id.ToString(), null, null, SafeIp(), null));
        await _db.SaveChangesAsync(cancellationToken);

        return new LoginResultDto
        {
            Token = token, ExpiresAtUtc = expiresAtUtc,
            RefreshToken = refreshToken, RefreshExpiresAtUtc = refreshExpiresAtUtc,
            Username = user.Username, DisplayName = user.DisplayName, Roles = roles
        };
    }

    private string? SafeIp()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }
}
