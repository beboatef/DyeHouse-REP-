using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Part 2 (password policy): the last few PBKDF2 hashes of a user, so a "new"
/// password that matches a recent one is refused. NEVER plaintext - each row is
/// verified with the same PBKDF2 Verify path as the live password, and only the
/// most recent <see cref="PasswordPolicy.MaxHistoryEntries"/> rows per user are
/// kept (older rows are pruned on every change).
/// </summary>
public class PasswordHistory : AuditableEntity
{
    public Guid UserId { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;

    private PasswordHistory() { } // EF Core

    public PasswordHistory(Guid userId, string passwordHash, string createdBy)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        UserId = userId;
        PasswordHash = passwordHash;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
