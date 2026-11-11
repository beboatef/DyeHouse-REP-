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

        // A rotated token showing up again usually means the old copy leaked: the legitimate client
        // replaced it, so whoever presents it now is holding a copy. The one benign explanation is
        // the concurrent-tab race, handled first by ReuseGraceAsync; everything else burns the whole
        // family and says nothing about which of the two cases this was.
        if (stored.RevokedAtUtc is not null)
        {
            var raced = await ReuseGraceAsync(stored, nowUtc, cancellationToken);
            if (raced is not null)
                return raced;

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

    /// <summary>
    /// Two tabs refresh in the same instant. Cookies are shared, but each tab built its request
    /// before the winner's response replaced them, so both carry the token that was current at
    /// that moment: one rotation wins and the loser arrives holding a token this server has just
    /// replaced. That is a race, not theft - and burning the family for it logged real users out.
    ///
    /// The grace is deliberately narrow: it only applies to a token revoked by rotation, only
    /// inside the configured window, and only while the successor it points at is still the live
    /// session. The caller gets a fresh access token but NO new refresh token, because the winner's
    /// response already set that cookie; re-issuing it here would race the other tab for the same
    /// cookie value. Everything outside those conditions keeps the strict rule.
    /// </summary>
    private async Task<RefreshSessionResultDto?> ReuseGraceAsync(
        UserRefreshToken stored, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var grace = _tokenService.RefreshReuseGrace;
        if (grace <= TimeSpan.Zero) return null;
        if (stored.RevokedReason != "Rotated" || stored.ReplacedByTokenHash is null) return null;
        if (stored.RevokedAtUtc is null) return null;
        if (nowUtc - stored.RevokedAtUtc.Value > grace) return null;

        // The chain must still be alive: after a logout or a real reuse detection the successor is
        // revoked (or expired), and a stale token must not be able to revive the session.
        var successor = await _db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == stored.ReplacedByTokenHash, cancellationToken);
        if (successor is null || !successor.IsLive(nowUtc)) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || !user.IsActive) return null;

        var roles = user.RoleList.ToList();
        var accessToken = _tokenService.GenerateToken(user.Id, user.Username, roles, out var accessExpiresAtUtc);

        _db.AuditLogEntries.Add(new AuditLogEntry(
            nowUtc, user.Username, "RefreshTokenReuseWithinGrace", nameof(UserRefreshToken),
            stored.UserId.ToString(), null, null, null,
            "A refresh token rotated moments ago was presented again; the live session was reused instead of revoked."));
        await _db.SaveChangesAsync(cancellationToken);

        return new RefreshSessionResultDto
        {
            Token = accessToken,
            ExpiresAtUtc = accessExpiresAtUtc,
            // Empty by design: the browser already holds the successor cookie.
            RefreshToken = string.Empty,
            RefreshExpiresAtUtc = successor.ExpiresAtUtc,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = roles
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

/// <summary>
/// The server-side half of logout when the request has no identity - the access token already
/// expired, which is the normal state after a tab sat idle past 30 minutes. The refresh cookie
/// itself names the session, and every live token of its owner dies, so a logout from an expired
/// session cannot leave a usable token behind for the rest of the seven-day lifetime.
/// An unknown or already-deleted token is a silent no-op: logout clears the browser either way.
/// </summary>
public record RevokeSessionByRefreshTokenCommand(string RefreshToken, string Reason) : IRequest<int>;

public class RevokeSessionByRefreshTokenCommandHandler : IRequestHandler<RevokeSessionByRefreshTokenCommand, int>
{
    private readonly IApplicationDbContext _db;

    public RevokeSessionByRefreshTokenCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<int> Handle(RevokeSessionByRefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return 0;

        var nowUtc = DateTime.UtcNow;
        var presentedHash = RefreshTokenCodec.Hash(request.RefreshToken);
        var stored = await _db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);
        if (stored is null)
            return 0;

        // Revoke the live family, not just the presented row: the cookie may be a token this server
        // rotated a moment ago, and the session that has to end is whatever replaced it.
        var live = await _db.UserRefreshTokens
            .Where(t => t.UserId == stored.UserId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in live)
            token.Revoke(request.Reason, null, nowUtc);
        await _db.SaveChangesAsync(cancellationToken);
        return live.Count;
    }
}
