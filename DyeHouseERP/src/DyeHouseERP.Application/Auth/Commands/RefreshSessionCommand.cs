using DyeHouseERP.Application.Auth.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Auth.Commands;

/// <summary>
/// Rotates the session: the presented refresh token dies here and a fresh pair takes its place.
/// The opaque token travels only in the HttpOnly cookie; the database holds just its hash, looked
/// up by unique index.
/// </summary>
public record RefreshSessionCommand(string RefreshToken) : IRequest<RefreshSessionResultDto>;

public class RefreshSessionCommandHandler : IRequestHandler<RefreshSessionCommand, RefreshSessionResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenService _tokenService;

    public RefreshSessionCommandHandler(IApplicationDbContext db, ITokenService tokenService)
    {
        _db = db; _tokenService = tokenService;
    }

    public async Task<RefreshSessionResultDto> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new InvalidCredentialsException();

        var presentedHash = RefreshTokenCodec.Hash(request.RefreshToken);
        var stored = await _db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

        // An unknown token is a blind guess at 256 bits of entropy: refuse it silently. Anything
        // this server issued is either live, expired, or already rotated - and handled below.
        if (stored is null)
            throw new InvalidCredentialsException();

        // A rotated token showing up again means the old copy leaked: the legitimate client
        // replaced it, so whoever presents it now is holding a copy. Burn the whole family and say
        // nothing about which of the two cases this was.
        if (stored.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(stored.UserId, "RefreshTokenReuseDetected", nowUtc, cancellationToken);
            var auditName = await _db.Users
                .Where(u => u.Id == stored.UserId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(cancellationToken)
                ?? stored.UserId.ToString();
            _db.AuditLogEntries.Add(new AuditLogEntry(
                nowUtc, auditName, "RefreshTokenReuseDetected", nameof(UserRefreshToken),
                stored.UserId.ToString(), null, null, null,
                "A rotated refresh token was presented again; every session of the user was revoked."));
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (stored.ExpiresAtUtc <= nowUtc)
        {
            stored.Revoke("Expired", null, nowUtc);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            stored.Revoke("UserInactive", null, nowUtc);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        var roles = user.RoleList.ToList();
        var accessToken = _tokenService.GenerateToken(user.Id, user.Username, roles, out var accessExpiresAtUtc);

        var newRefreshToken = RefreshTokenCodec.NewToken();
        var newRefreshHash = RefreshTokenCodec.Hash(newRefreshToken);
        var newRefreshExpiresAtUtc = nowUtc.Add(_tokenService.RefreshTokenLifetime);

        stored.Revoke("Rotated", newRefreshHash, nowUtc);
        _db.UserRefreshTokens.Add(new UserRefreshToken(user.Id, newRefreshHash, newRefreshExpiresAtUtc));

        // Opportunistic janitor: drop tokens that died more than 30 days ago so the table cannot
        // grow forever. Capped, so one user with a huge history cannot stall their own refresh.
        var stale = await _db.UserRefreshTokens
            .Where(t => t.UserId == user.Id && t.ExpiresAtUtc < nowUtc.AddDays(-30))
            .Take(100)
            .ToListAsync(cancellationToken);
        _db.UserRefreshTokens.RemoveRange(stale);

        await _db.SaveChangesAsync(cancellationToken);

        return new RefreshSessionResultDto
        {
            Token = accessToken, ExpiresAtUtc = accessExpiresAtUtc,
            RefreshToken = newRefreshToken, RefreshExpiresAtUtc = newRefreshExpiresAtUtc,
            Username = user.Username, DisplayName = user.DisplayName, Roles = roles
        };
    }

    private async Task RevokeFamilyAsync(Guid userId, string reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var live = await _db.UserRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in live)
            token.Revoke(reason, null, nowUtc);
    }
}

/// <summary>Revokes every live refresh token of a user - the server-side half of logout.</summary>
public record RevokeUserRefreshTokensCommand(Guid UserId, string Reason) : IRequest<int>;

public class RevokeUserRefreshTokensCommandHandler : IRequestHandler<RevokeUserRefreshTokensCommand, int>
{
    private readonly IApplicationDbContext _db;

    public RevokeUserRefreshTokensCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<int> Handle(RevokeUserRefreshTokensCommand request, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var live = await _db.UserRefreshTokens
            .Where(t => t.UserId == request.UserId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in live)
            token.Revoke(request.Reason, null, nowUtc);
        await _db.SaveChangesAsync(cancellationToken);
        return live.Count;
    }
}
