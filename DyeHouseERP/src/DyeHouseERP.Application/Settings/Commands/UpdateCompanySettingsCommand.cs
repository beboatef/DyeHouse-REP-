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
        // Keep the stored logo to a sane size (~2MB base64) so a giant upload can't bloat every settings read.
        RuleFor(x => x.LogoDataUrl).Must(x => x is null || x.Length < 2_800_000)
            .WithMessage("Logo image is too large - please use a smaller file (under ~2MB).");
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
