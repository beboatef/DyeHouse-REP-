using DyeHouseERP.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the initial production EXECUTION shifts (S-Production-Execution-Shifts).
///
/// These are execution data only: the shift a stage execution ran on. They are
/// NEVER the HR employee shift model and are never connected to attendance,
/// check-in/out or payroll.
///
/// The three rows below are initial master data, not a limit - the factory can
/// rename them, deactivate them or add more from inside the ERP, and every
/// operation (current and future) can be executed on any of them.
///
/// Idempotent by Code: an existing shift is never overwritten.
/// </summary>
public static class ProductionShiftSeeder
{
    private static readonly (string Code, string Name)[] Shifts =
    {
        ("SHIFT-1", "الوردية الأولى"),
        ("SHIFT-2", "الوردية الثانية"),
        ("SHIFT-3", "الوردية الثالثة")
    };

    public static async Task SeedAsync(
        ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existingCodes = db.ProductionShifts.Select(s => s.Code).ToHashSet();

        var added = 0;
        var sequence = 1;

        foreach (var (code, name) in Shifts)
        {
            if (!existingCodes.Contains(code))
            {
                db.ProductionShifts.Add(new ProductionShift(code, name, sequence, createdBy: "seed"));
                added++;
            }

            sequence++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} production execution shifts.", added);
        }
    }
}
