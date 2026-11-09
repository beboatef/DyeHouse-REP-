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
/// الطباعة (PRINTING) is NOT one of those twenty-one operations. It is a SEPARATE,
/// REPEATABLE PRINTING RUN recorded against a Job Order - one execution per drawing, each
/// carrying its own drawing number - so it is seeded under its own stable code
/// (<see cref="PrintingRunCode"/>, deliberately never <c>PS022</c>) and it never counts
/// towards <see cref="ApprovedOperationCount"/>. The approved operations are run once each
/// per order and carry no drawing number; the printing run is the only row that may repeat
/// and the only row that demands one.
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

    /// <summary>
    /// Stable code of the separate printing run. Deliberately NOT a position code: the
    /// printing run is not operation number twenty-two, and adding or renaming it must never
    /// renumber the approved twenty-one.
    /// </summary>
    public const string PrintingRunCode = "PRINTING";

    /// <summary>The printing run's name. It is a run, not one of the approved operations.</summary>
    public const string PrintingRunName = "الطباعة";

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
                    notes: null,
                    isFormationStage: name == "التشكيل",
                    isReadyGoodsStage: name == "الجاهز"));

                added++;
            }

            sequence++;
        }

        // The printing run is seeded AFTER the approved list and under its own code, so it is
        // never mistaken for operation #22 and the twenty-one keep their positional codes. Its
        // Sequence is display order only, like every other stage's - it imposes no route and is
        // copied onto an execution only for ordering. What makes it a run rather than an
        // operation is behaviour, not a number: it is the only stage that demands a drawing
        // number, and therefore the only one the transfer workflow lets a Job Order repeat.
        if (!existingCodes.Contains(PrintingRunCode))
        {
            db.ProductionStageDefinitions.Add(new ProductionStageDefinition(
                PrintingRunCode, PrintingRunName, sequence, "seed", requiresDrawingNumber: true));
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
