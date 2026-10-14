using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Test double for <see cref="ICompanyIdentityProvider"/> so export/report tests can control the
/// company identity without a database.
/// </summary>
public sealed class TestCompanyIdentityProvider : ICompanyIdentityProvider
{
    public TestCompanyIdentityProvider(string nameAr, string nameEn, string? logoDataUrl = null)
        => Identity = new CompanyIdentity(nameAr, nameEn, logoDataUrl);

    public TestCompanyIdentityProvider(CompanyIdentity identity) => Identity = identity;

    /// <summary>A minimal valid 1x1 PNG data URL, for "logo present" cases.</summary>
    public const string OnePixelPngDataUrl =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public CompanyIdentity Identity { get; set; }

    public CompanyIdentity Get() => Identity;
}
