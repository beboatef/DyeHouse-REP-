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

        var adminPasswordHash = passwordHasher.Hash(DefaultPassword);
        var admin = new User(
            DefaultUsername,
            adminPasswordHash,
            "Administrator",
            roles: $"admin,{Permissions.InventoryAllowNegativeStock}",
            createdBy: "system");

        // Part 2: the development default password is public knowledge (it is in this
        // file). The account starts flagged so the first thing anyone must do is replace it.
        admin.MarkPasswordSetByAdministrator("system");
        context.Users.Add(admin);
        context.PasswordHistories.Add(new PasswordHistory(admin.Id, adminPasswordHash, "system"));
        await context.SaveChangesAsync(cancellationToken);
    }
}
