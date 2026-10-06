using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the default purchase units required by spec section 44, so a fresh
/// install already offers وحدة / عدد / قطعة / كرتونة / عبوة / متر / كيلو / بوصة /
/// نصف بوصة without the admin having to create them by hand.
///
/// None of them carries a conversion factor: conversion is explicit and opt-in
/// (spec section 44), so a default unit that "knows" how to become another unit
/// would be exactly the automatic conversion the spec forbids.
///
/// Safe to run on every startup: a unit is only inserted when its code is not
/// already present, so renaming or deactivating a default unit is never
/// overwritten by a later run.
/// </summary>
public static class PurchaseUnitSeeder
{
    /// <summary>The nine defaults from spec section 44, in the order they are listed there.</summary>
    public static readonly IReadOnlyList<(string Code, string NameAr, string NameEn)> Defaults = new[]
    {
        ("UNIT",      "وحدة",        "Unit"),
        ("COUNT",     "عدد",         "Count"),
        ("PIECE",     "قطعة",        "Piece"),
        ("CARTON",    "كرتونة",      "Carton"),
        ("PACK",      "عبوة",        "Pack"),
        ("METER",     "متر",         "Meter"),
        ("KG",        "كيلو",        "Kilogram"),
        ("INCH",      "بوصة",        "Inch"),
        ("HALF_INCH", "نصف بوصة",    "Half Inch")
    };

    public static async Task SeedAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        var existingCodes = await context.PurchaseUnits
            .Select(u => u.Code)
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var (code, nameAr, nameEn) in Defaults)
        {
            if (existingCodes.Contains(code)) continue;
            context.PurchaseUnits.Add(new PurchaseUnit(code, nameAr, nameEn, "system", isSystemDefault: true));
            added = true;
        }

        if (added)
            await context.SaveChangesAsync(cancellationToken);
    }
}
