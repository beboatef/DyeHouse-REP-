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
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
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
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
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

        _db.AuditLogEntries.Add(new AuditLogEntry(
            DateTime.UtcNow, user.Username, "Login", nameof(User), user.Id.ToString(), null, null, SafeIp(), null));
        await _db.SaveChangesAsync(cancellationToken);

        return new LoginResultDto
        {
            Token = token, ExpiresAtUtc = expiresAtUtc,
            Username = user.Username, DisplayName = user.DisplayName, Roles = roles
        };
    }

    private string? SafeIp()
    {
        try { return _currentUser.IpAddress; } catch { return null; }
    }
}
