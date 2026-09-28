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
