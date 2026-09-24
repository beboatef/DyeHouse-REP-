using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DyeHouseERP.Infrastructure;

public class SystemDateTime : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddScoped<IExcelImportReader, ExcelImportReader>();
        services.AddScoped<IExcelReaderService, ExcelReaderService>();

        return services;
    }
}
