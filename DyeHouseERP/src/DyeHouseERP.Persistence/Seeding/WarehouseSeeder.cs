using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the default warehouse structure required by spec section 15, so a
/// fresh install is usable without the admin having to hand-create the five
/// stores first:
///   1. Raw Material Warehouse   (customer-owned raw material, as received)
///   2. Production / WIP         (customer-owned material in process)
///   3. Ready Goods              (finished, customer-owned, awaiting delivery)
///   4. Materials / Chemicals    (factory-owned chemicals and dyes)
///   5. Operating Supplies       (factory-owned spare parts / packaging / consumables)
///
/// Safe to run on every startup: only inserts a default warehouse when a
/// warehouse with that code does not already exist, so renaming, deactivating
/// or restructuring warehouses in the admin screen is never overwritten.
/// </summary>
public static class WarehouseSeeder
{
    private static readonly (string Code, string Name, WarehouseKind Kind)[] Defaults =
    {
        ("WH-RAW",  "مخزن الخام",              WarehouseKind.RawMaterial),
        ("WH-WIP",  "الإنتاج / تحت التشغيل",   WarehouseKind.ProductionWip),
        ("WH-RDY",  "المخزون الجاهز",          WarehouseKind.ReadyGoods),
        ("WH-MAT",  "المواد والكيماويات",      WarehouseKind.Materials),
        ("WH-SUP",  "مستلزمات التشغيل",        WarehouseKind.OperatingSupplies)
    };

    public static async Task SeedAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        var existingCodes = await context.Warehouses
            .Select(w => w.Code)
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var (code, name, kind) in Defaults)
        {
            if (existingCodes.Contains(code)) continue;
            context.Warehouses.Add(new Warehouse(code, name, kind, "system"));
            added = true;
        }

        if (added)
            await context.SaveChangesAsync(cancellationToken);
    }
}
