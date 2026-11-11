using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Application login identity. Deliberately minimal - roles are a simple
/// comma-separated string checked against Permissions constants (e.g.
/// "inventory.allow_negative_stock") rather than a full claims/policy
/// system; see README "Honest gaps" for the tradeoff.
/// </summary>
public class User : AuditableEntity
{
    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Roles { get; private set; } = string.Empty; // comma-separated, e.g. "admin,inventory.allow_negative_stock"
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Part 2 (password policy): while set, the API middleware blocks every endpoint except
    /// change-password, logout and the current-user read. Set for the bootstrap admin, for
    /// every user an administrator creates or resets, and when the password age exceeds
    /// CompanySettings.PasswordMaxAgeDays. Only a successful password change clears it.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>UTC timestamp of the last creation/reset/change - the base for password expiry.</summary>
    public DateTime? PasswordChangedAtUtc { get; private set; }

    private User() { } // EF Core

    public User(string username, string passwordHash, string displayName, string roles, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        Username = username.Trim();
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Roles = roles;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public IEnumerable<string> RoleList => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void ChangePassword(string newPasswordHash, string modifiedBy)
    {
        PasswordHash = newPasswordHash;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a password set outside the user's own change (bootstrap, admin create/reset):
    /// the password is new, but the user must still pick their own at the next sign-in.
    /// </summary>
    public void MarkPasswordSetByAdministrator(string modifiedBy)
    {
        MustChangePassword = true;
        PasswordChangedAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Clears the forced-change flag after the user successfully picked their own password.</summary>
    public void MarkPasswordChangedByUser(string newPasswordHash, string modifiedBy)
    {
        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        PasswordChangedAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Flags the user when the password age exceeds the configured maximum (login still succeeds).</summary>
    public void FlagExpiredPassword(string modifiedBy)
    {
        MustChangePassword = true;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }

    /// <summary>
    /// Changes the display name and the permission/role list (spec section 41).
    /// Roles stay a flat comma-separated list of permission names - this is the
    /// existing model, intentionally NOT replaced by a Role table.
    /// </summary>
    public void SetProfile(string displayName, IEnumerable<string> roles, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));

        DisplayName = displayName.Trim();

        // Normalise: trim, drop blanks, de-duplicate, keep a stable (sorted)
        // order so two identical permission sets always serialise the same way.
        Roles = string.Join(",", roles
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase));

        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
