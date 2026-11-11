using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Single-row company branding/settings (spec section 47 "Administration ->
/// Settings"). Logo is stored as a base64 data URL directly in the row -
/// simplest reliable option without standing up separate blob storage, and
/// small enough (a logo image) that this is a reasonable tradeoff. There is
/// always exactly one row; CompanySettingsSeeder creates it if missing.
/// </summary>
public class CompanySettings : AuditableEntity
{
    public string CompanyNameAr { get; private set; } = "DyeHouse ERP";
    public string CompanyNameEn { get; private set; } = "DyeHouse ERP";
    public string? LogoDataUrl { get; private set; } // e.g. "data:image/png;base64,...."

    /// <summary>
    /// Part 2 (password policy): maximum password age in days. 0 (default) disables expiry.
    /// When set, a login whose password is older than this succeeds but flags the account
    /// must-change, so the next thing the user can do is pick a new password.
    /// </summary>
    public int PasswordMaxAgeDays { get; private set; }

    /// <summary>
    /// Part 2 (password policy): idle session limit in minutes (default 30). A refresh is
    /// refused when the presented refresh token's last activity is older than this.
    /// Activity is tracked on the refresh-token row itself, never in a session cookie flag.
    /// </summary>
    public int SessionIdleMinutes { get; private set; } = 30;

    private CompanySettings() { } // EF Core

    public CompanySettings(string createdBy)
    {
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string companyNameAr, string companyNameEn, string? logoDataUrl, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(companyNameAr)) throw new ArgumentException("Arabic company name is required.", nameof(companyNameAr));
        if (string.IsNullOrWhiteSpace(companyNameEn)) throw new ArgumentException("English company name is required.", nameof(companyNameEn));

        CompanyNameAr = companyNameAr;
        CompanyNameEn = companyNameEn;
        LogoDataUrl = logoDataUrl;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the password/session policy (spec: Part 2). Bounds keep a typo from silently
    /// disabling a security control: PasswordMaxAgeDays 0 disables expiry, 1..1825 bounds a
    /// real policy; SessionIdleMinutes below 5 falls back to the 30-minute default.
    /// </summary>
    public void UpdatePasswordPolicy(int passwordMaxAgeDays, int sessionIdleMinutes, string modifiedBy)
    {
        if (passwordMaxAgeDays < 0 || passwordMaxAgeDays > 1825)
            throw new ArgumentException("Password max age must be 0 (disabled) or 1..1825 days.", nameof(passwordMaxAgeDays));

        PasswordMaxAgeDays = passwordMaxAgeDays;
        SessionIdleMinutes = sessionIdleMinutes < 5 ? 30 : sessionIdleMinutes;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
