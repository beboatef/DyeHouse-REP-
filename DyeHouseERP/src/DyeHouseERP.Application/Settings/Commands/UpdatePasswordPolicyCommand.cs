using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Settings.DTOs;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Settings.Commands;

/// <summary>
/// Part 2 (password policy): tunes the two knobs. Kept separate from the branding command so
/// the existing update shape never changes: PasswordMaxAgeDays 0 disables expiry, otherwise
/// 1..1825 days; SessionIdleMinutes below 5 falls back to the 30-minute default (see
/// CompanySettings.UpdatePasswordPolicy for the bounds).
/// </summary>
public record UpdatePasswordPolicyCommand(int PasswordMaxAgeDays, int SessionIdleMinutes) : IRequest<CompanySettingsDto>;

public class UpdatePasswordPolicyCommandValidator : AbstractValidator<UpdatePasswordPolicyCommand>
{
    public UpdatePasswordPolicyCommandValidator()
    {
        RuleFor(x => x.PasswordMaxAgeDays).InclusiveBetween(0, 1825)
            .WithMessage("Password max age must be 0 (disabled) or 1..1825 days.");
        RuleFor(x => x.SessionIdleMinutes).InclusiveBetween(5, 43200)
            .WithMessage("Session idle timeout must be 5..43200 minutes.");
    }
}

public class UpdatePasswordPolicyCommandHandler : IRequestHandler<UpdatePasswordPolicyCommand, CompanySettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdatePasswordPolicyCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<CompanySettingsDto> Handle(UpdatePasswordPolicyCommand request, CancellationToken cancellationToken)
    {
        var settings = await _db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new CompanySettings(_currentUser.UserName);
            _db.CompanySettings.Add(settings);
        }

        settings.UpdatePasswordPolicy(request.PasswordMaxAgeDays, request.SessionIdleMinutes, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return new CompanySettingsDto
        {
            CompanyNameAr = settings.CompanyNameAr, CompanyNameEn = settings.CompanyNameEn, LogoDataUrl = settings.LogoDataUrl,
            PasswordMaxAgeDays = settings.PasswordMaxAgeDays, SessionIdleMinutes = settings.SessionIdleMinutes
        };
    }
}
