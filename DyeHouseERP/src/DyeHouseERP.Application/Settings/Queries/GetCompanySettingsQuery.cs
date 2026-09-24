using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Settings.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Settings.Queries;

/// <summary>
/// Deliberately allowed anonymous (see SettingsController) - the login
/// page needs the company name/logo before the user has a token.
/// </summary>
public record GetCompanySettingsQuery : IRequest<CompanySettingsDto>;

public class GetCompanySettingsQueryHandler : IRequestHandler<GetCompanySettingsQuery, CompanySettingsDto>
{
    private readonly IApplicationDbContext _db;
    public GetCompanySettingsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CompanySettingsDto> Handle(GetCompanySettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        return new CompanySettingsDto
        {
            CompanyNameAr = settings?.CompanyNameAr ?? "DyeHouse ERP",
            CompanyNameEn = settings?.CompanyNameEn ?? "DyeHouse ERP",
            LogoDataUrl = settings?.LogoDataUrl
        };
    }
}
