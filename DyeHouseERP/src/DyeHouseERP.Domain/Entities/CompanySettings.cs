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
}
