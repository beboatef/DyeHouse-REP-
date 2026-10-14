using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Settings.DTOs;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Settings.Commands;

/// <summary>Updates the company's display name and logo (spec section 1 "branding", section 47 "Settings"). Admin only - see SettingsController.</summary>
public record UpdateCompanySettingsCommand(string CompanyNameAr, string CompanyNameEn, string? LogoDataUrl) : IRequest<CompanySettingsDto>;

public class UpdateCompanySettingsCommandValidator : AbstractValidator<UpdateCompanySettingsCommand>
{
    public UpdateCompanySettingsCommandValidator()
    {
        RuleFor(x => x.CompanyNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CompanyNameEn).NotEmpty().MaximumLength(200);

        // Logo upload validation (spec section 47): only a real image data URL of an accepted
        // type is stored, and only up to a sane size, so a bad upload is refused at the door
        // rather than stored and later breaking every document that embeds it.
        RuleFor(x => x.LogoDataUrl)
            .Must(x => x is null || string.IsNullOrWhiteSpace(x) || IsValidImageDataUrl(x))
            .WithMessage("The logo must be an image (PNG, JPEG, GIF or WebP) sent as a data URL.");
        RuleFor(x => x.LogoDataUrl)
            .Must(x => x is null || x.Length < 2_800_000)
            .WithMessage("Logo image is too large - please use a smaller file (under ~2MB).");
    }

    /// <summary>
    /// Accepts only a data URL whose MIME is an allowed image type and whose payload is valid
    /// base64. Kept as a static helper so it can be unit-tested without a validator context.
    /// </summary>
    public static bool IsValidImageDataUrl(string value)
    {
        var allowed = new[] { "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp" };
        const string marker = ";base64,";

        var trimmed = value.Trim();
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || markerIndex < 0)
            return false;

        var mime = trimmed.Substring("data:".Length, markerIndex - "data:".Length).Trim().ToLowerInvariant();
        if (!allowed.Contains(mime))
            return false;

        var payload = trimmed[(markerIndex + marker.Length)..];
        try { return Convert.FromBase64String(payload).Length > 0; }
        catch (FormatException) { return false; }
    }
}

public class UpdateCompanySettingsCommandHandler : IRequestHandler<UpdateCompanySettingsCommand, CompanySettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateCompanySettingsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<CompanySettingsDto> Handle(UpdateCompanySettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _db.CompanySettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new CompanySettings(_currentUser.UserName);
            _db.CompanySettings.Add(settings);
        }

        settings.Update(request.CompanyNameAr, request.CompanyNameEn, request.LogoDataUrl, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return new CompanySettingsDto { CompanyNameAr = settings.CompanyNameAr, CompanyNameEn = settings.CompanyNameEn, LogoDataUrl = settings.LogoDataUrl };
    }
}
