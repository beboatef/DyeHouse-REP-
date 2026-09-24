using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Numbering;

/// <summary>
/// Seeds a single Development-only admin login (admin / Admin@12345) so the
/// API and frontend are usable immediately without a separate user-
/// management screen. Does nothing if any user already exists - never
/// resets a real deployment's users.
/// </summary>
public static class UserSeeder
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "Admin@12345";

    public static async Task SeedAsync(ApplicationDbContext context, IPasswordHasher passwordHasher, CancellationToken cancellationToken = default)
    {
        if (await context.Users.AnyAsync(cancellationToken)) return;

        var admin = new User(
            DefaultUsername,
            passwordHasher.Hash(DefaultPassword),
            "Administrator",
            roles: $"admin,{Permissions.InventoryAllowNegativeStock},{Permissions.StageRequiresApproval}",
            createdBy: "system");

        context.Users.Add(admin);
        await context.SaveChangesAsync(cancellationToken);
    }
}
