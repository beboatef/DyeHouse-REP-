using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

/// <summary>
/// Reads the single CompanySettings row and exposes it as the authoritative identity for
/// every document/export (spec section 47). Deliberately forgiving: a missing row (a fresh
/// database that has never been configured) or a read failure falls back to the built-in
/// default rather than failing a PDF/Excel export - "missing logo/settings must not break
/// generation". It re-reads on every call, so an updated name/logo is reflected immediately
/// with no stale cache.
/// </summary>
public class CompanyIdentityProvider : ICompanyIdentityProvider
{
    /// <summary>Used only until an administrator configures the company name.</summary>
    public const string DefaultName = "DyeHouse ERP";

    private readonly ApplicationDbContext _context;

    public CompanyIdentityProvider(ApplicationDbContext context) => _context = context;

    public CompanyIdentity Get()
    {
        try
        {
            var settings = _context.CompanySettings.AsNoTracking().FirstOrDefault();
            if (settings is null)
                return new CompanyIdentity(DefaultName, DefaultName, null);

            var ar = string.IsNullOrWhiteSpace(settings.CompanyNameAr) ? DefaultName : settings.CompanyNameAr;
            var en = string.IsNullOrWhiteSpace(settings.CompanyNameEn) ? DefaultName : settings.CompanyNameEn;
            return new CompanyIdentity(ar, en, settings.LogoDataUrl);
        }
        catch
        {
            // Never let branding retrieval break a report/export.
            return new CompanyIdentity(DefaultName, DefaultName, null);
        }
    }
}
