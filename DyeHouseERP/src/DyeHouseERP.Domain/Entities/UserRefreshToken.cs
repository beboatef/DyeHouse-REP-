namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// One leg of a login session. The access JWT in the HttpOnly cookie is short-lived; this row is
/// what lets the client obtain the next access token without asking for the password again.
///
/// Only the SHA-256 hash of the opaque token is stored, never the token itself, so a database
/// read yields no usable session. Rotation replaces the row on every use: a stolen copy is either
/// already dead, or its reuse proves theft and burns the whole family
/// (see RefreshSessionCommandHandler).
/// </summary>
public class UserRefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? RevokedReason { get; private set; }

    private UserRefreshToken() { } // EF Core

    public UserRefreshToken(Guid userId, string tokenHash, DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new ArgumentException("Token hash is required.", nameof(tokenHash));

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsLive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    /// <summary>First revocation wins, so a rotated token keeps pointing at its replacement.</summary>
    public void Revoke(string reason, string? replacedByTokenHash, DateTime nowUtc)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = nowUtc;
        RevokedReason = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
