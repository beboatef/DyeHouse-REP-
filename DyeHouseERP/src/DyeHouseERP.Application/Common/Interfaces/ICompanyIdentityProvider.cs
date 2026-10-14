namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// The factory's own identity, used by every printed/exported document (spec section 47).
/// This is the SINGLE authoritative source: invoices, delivery notes, statements, PDF reports
/// and Excel exports all read it instead of carrying a hard-coded company name or logo.
/// </summary>
public sealed record CompanyIdentity(string NameAr, string NameEn, string? LogoDataUrl)
{
    /// <summary>English name when present, otherwise the Arabic one - never empty (a default is applied upstream).</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(NameEn) ? NameAr : NameEn;

    /// <summary>True when a (non-blank) logo data URL is configured.</summary>
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoDataUrl);

    /// <summary>
    /// Best-effort decode of the logo data URL ("data:image/png;base64,...") into raw bytes.
    /// Returns false - never throws - for a missing, blank, malformed or non-image value, so a
    /// broken logo can never break document generation.
    /// </summary>
    public bool TryGetLogoBytes(out byte[] bytes, out string contentType)
    {
        bytes = Array.Empty<byte>();
        contentType = "image/png";

        if (!HasLogo) return false;

        var value = LogoDataUrl!.Trim();
        const string marker = ";base64,";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || markerIndex < 0)
            return false;

        var mime = value.Substring("data:".Length, markerIndex - "data:".Length).Trim();
        if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return false;

        var payload = value[(markerIndex + marker.Length)..];
        try
        {
            bytes = Convert.FromBase64String(payload);
            if (bytes.Length == 0) return false;
        }
        catch (FormatException)
        {
            return false;
        }

        contentType = mime;
        return true;
    }
}

/// <summary>
/// Resolves the single company identity row. Implemented in Persistence (reads CompanySettings);
/// kept behind this interface so the export layer never touches the DbContext directly and a
/// test can supply its own identity.
/// </summary>
public interface ICompanyIdentityProvider
{
    /// <summary>Reads the current identity. Must never throw for a missing row - a built-in default is returned instead.</summary>
    CompanyIdentity Get();
}
