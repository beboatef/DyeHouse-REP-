using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Monitoring;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// Part 2 (password policy): the single implementation behind every password set - self
/// change, admin create and admin reset - so the rules cannot drift between endpoints.
/// Each set: enforces <see cref="PasswordPolicy"/>, stores the PBKDF2 hash (never plaintext),
/// appends to <see cref="PasswordHistory"/> (pruned to the last entries), revokes every live
/// refresh token of the user with reason "PasswordChanged", writes an audit entry whose
/// before/after payloads are ALWAYS null (a hash is still a secret), and emits the
/// <see cref="LogEvents.RefreshTokenRevokedForPasswordChange"/> event.
/// </summary>
public static class PasswordChangeService
{
    /// <param name="userInitiated">
    /// True when the user picked the password themselves (clears MustChangePassword);
    /// false when an administrator set it (sets MustChangePassword, forcing the user to
    /// pick their own at the next sign-in).
    /// </param>
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        User user,
        string newPassword,
        string actorUsername,
        string? actorIpAddress,
        bool userInitiated,
        IPasswordHasher passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var historyHashes = await db.PasswordHistories.AsNoTracking()
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Take(PasswordPolicy.MaxHistoryEntries)
            .Select(h => h.PasswordHash)
            .ToListAsync(cancellationToken);

        PasswordPolicy.EnsureValid(
            nameof(newPassword), user.Username, newPassword,
            user.PasswordHash, historyHashes, passwordHasher);

        var newHash = passwordHasher.Hash(newPassword);

        if (userInitiated)
            user.MarkPasswordChangedByUser(newHash, actorUsername);
        else
        {
            user.ChangePassword(newHash, actorUsername);
            user.MarkPasswordSetByAdministrator(actorUsername);
        }

        db.PasswordHistories.Add(new PasswordHistory(user.Id, newHash, actorUsername));

        // Prune: the just-added row is not in the database yet (SaveChanges runs at the end),
        // so the newest MaxHistoryEntries-1 EXISTING rows stay and this change lands the total
        // back on MaxHistoryEntries. The live password is compared separately at validation.
        var prune = await db.PasswordHistories
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedAtUtc)
            .ThenByDescending(h => h.Id)
            .Skip(PasswordPolicy.MaxHistoryEntries - 1)
            .ToListAsync(cancellationToken);
        db.PasswordHistories.RemoveRange(prune);

        var live = await db.UserRefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in live)
            token.Revoke("PasswordChanged", null, DateTime.UtcNow);

        db.AuditLogEntries.Add(new AuditLogEntry(
            DateTime.UtcNow, actorUsername,
            userInitiated ? "PasswordChanged" : "PasswordReset",
            nameof(User), user.Id.ToString(), null, null, actorIpAddress,
            userInitiated ? "The user changed their own password." : "An administrator reset the password."));

        logger.LogWarning(
            "EventName: {EventName}. {Count} refresh token(s) of user {Username} revoked: password changed.",
            LogEvents.RefreshTokenRevokedForPasswordChange, live.Count, user.Username);

        await db.SaveChangesAsync(cancellationToken);
    }
}
