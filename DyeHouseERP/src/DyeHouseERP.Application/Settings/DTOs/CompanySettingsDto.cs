namespace DyeHouseERP.Application.Settings.DTOs;

public class CompanySettingsDto
{
    public string CompanyNameAr { get; set; } = string.Empty;
    public string CompanyNameEn { get; set; } = string.Empty;
    public string? LogoDataUrl { get; set; }
    /// <summary>Part 2: 0 = expiry disabled.</summary>
    public int PasswordMaxAgeDays { get; set; }
    /// <summary>Part 2: idle session limit in minutes (default 30).</summary>
    public int SessionIdleMinutes { get; set; } = 30;
}
