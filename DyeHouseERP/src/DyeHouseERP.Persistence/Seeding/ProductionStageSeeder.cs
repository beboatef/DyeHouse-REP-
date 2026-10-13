using DyeHouseERP.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the factory's real process stages as SELECTABLE master data.
///
/// Exactly the twenty-one approved OPERATIONS - the agreed, fixed list. Rework is a
/// capability of every operation and the execution shift is execution data, so NEITHER
/// is a stage row: the "اعادة" (redo) and "وردية" (shift) variants are deliberately not
/// seeded (spec sections 18 and 54).
///
/// These are not a mandatory route. Only two stages carry a route marker: التشكيل
/// (every Job Order starts there) and الجاهز (the stage that moves output to Ready
/// Goods). Every other stage is simply available for the operator to select at transfer
/// time (spec section 13).
///
/// A redo is an explicit capability of the operation being run rather than a Job Order moving
/// backwards, which keeps a redo from being counted twice; that is why no "rework" flag exists on a
/// stage. Only <c>AllowSkip</c> is carried, because only SkipStageCommand actually reads it. A printing
/// stage may still run more than once - driven by its own RequiresDrawingNumber semantics through the
/// ordinary transfer workflow (each run carries its own drawing number and quantities).
///
/// Seeding is idempotent by Code: an existing stage is never modified or overwritten, so
/// an administrator's renames and deactivations survive.
/// </summary>
public static class ProductionStageSeeder
{
    /// <summary>
    /// The approved operation list, verbatim. Order defines the display order only
    /// (Sequence); it does NOT impose a route on any Job Order.
    /// </summary>
    public static readonly string[] StageNames =
    {
        "استلام",
        "التشكيل",
        "الجاهز",
        "الجيت",
        "الرام التثبيت",
        "الرام التجهيز",
        "الفرادة",
        "اللف",
        "انكماش وتكسير",
        "بيتش",
        "روتاري",
        "سوفلينا",
        "غسيل سوفلينا",
        "عصارة مفتوح",
        "عصارة مقفول",
        "كسترا",
        "كلندر",
        "كومباكتور",
        "مثبت حراري",
        "مثبت مائي",
        "مجفف"
    };

    /// <summary>
    /// How many operations the approved list holds. This is a starting point, never a
    /// maximum: a new stage is added from inside the ERP with no source change.
    /// </summary>
    public static int ApprovedOperationCount => StageNames.Length;

    public static async Task SeedAsync(
        ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existingCodes = db.ProductionStageDefinitions
            .Select(s => s.Code)
            .ToHashSet();

        var added = 0;
        var sequence = 1;

        foreach (var name in StageNames)
        {
            // A stable, length-safe code derived from the position in the list.
            // Never derived from the (Arabic) name, so renaming in the UI cannot
            // orphan the row.
            var code = $"PS{sequence:000}";

            if (!existingCodes.Contains(code))
            {
                db.ProductionStageDefinitions.Add(new ProductionStageDefinition(
                    code: code,
                    name: name,
                    sequence: sequence,
                    createdBy: "seed",
                    requiresInputQuantity: true,
                    requiresOutputQuantity: true,
                    // No skipping: every stage the operator runs must keep its
                    // baseline so the loss chain stays intact.
                    allowSkip: false,
                    notes: null,
                    isFormationStage: name == "التشكيل",
                    isReadyGoodsStage: name == "الجاهز"));

                added++;
            }

            sequence++;
        }

        // Separate stable code: adding Printing never renumbers the original 21 operations.
        if (!existingCodes.Contains("PRINTING"))
        {
            db.ProductionStageDefinitions.Add(new ProductionStageDefinition(
                "PRINTING", "الطباعة", sequence, "seed", requiresDrawingNumber: true));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} production stages.", added);
        }
        else
        {
            logger.LogInformation("All production stages already present; nothing seeded.");
        }
    }
}
