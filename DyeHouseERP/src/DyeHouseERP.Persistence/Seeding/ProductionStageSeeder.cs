using DyeHouseERP.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the factory's real process stages as SELECTABLE master data.
///
/// These are deliberately NOT a mandatory route. Only two stages carry a route
/// marker: التشكيل (every Job Order starts there) and الجاهز (the stage that
/// moves output to Ready Goods). Every other stage - including the "اعادة"
/// (redo) and "وردية" (shift) variants - is simply available for the operator to
/// select at transfer time (spec sections 13 and 54).
///
/// The redo/shift distinctions already have a proper representation in this list,
/// so <c>AllowRework</c> is left FALSE throughout: rework is now expressed by
/// choosing an explicit "اعادة" stage rather than by moving a Job Order
/// backwards, which keeps a redo from being counted twice.
///
/// Seeding is idempotent by Code: an existing stage is never modified or
/// overwritten, so an administrator's renames and deactivations survive.
/// </summary>
public static class ProductionStageSeeder
{
    /// <summary>
    /// The stage list exactly as supplied by the factory. Order defines the
    /// display order only (Sequence); it does NOT impose a route on any Job Order.
    /// </summary>
    private static readonly string[] StageNames =
    {
        // ---- Entry, formation and the final stage ----
        "استلام",
        "التشكيل",
        "الجاهز",
        "الجيت",

        // ---- Ram fixation ----
        "الرام التثبيت",
        "الرام التثبيت1 اعادة",
        "الرام التثبيت1 وردية أولى",
        "الرام التثبيت1 وردية ثانية",
        "الرام التثبيت1 وردية ثالثة",
        "الرام التثبيت2 اعادة",
        "الرام التثبيت2 وردية أولى",
        "الرام التثبيت2 وردية ثانية",
        "الرام التثبيت2 وردية ثالثة",
        "الرام التثبيت3 اعادة",
        "الرام التثبيت3 وردية أولى",
        "الرام التثبيت3 وردية ثانية",
        "الرام التثبيت3 وردية ثالثة",
        "الرام التثبيت4 اعادة",
        "الرام التثبيت4 وردية أولى",
        "الرام التثبيت4 وردية ثانية",
        "الرام التثبيت4 وردية ثالثة",

        // ---- Ram finishing ----
        "الرام التجهيز",
        "الرام التجهيز 1 اعادة",
        "الرام التجهيز 1 وردية أولى",
        "الرام التجهيز 1 وردية ثانية",
        "الرام التجهيز 1 وردية ثالثة",
        "الرام التجهيز 2 اعادة",
        "الرام التجهيز 2 وردية أولى",
        "الرام التجهيز 2 وردية ثانية",
        "الرام التجهيز 2 وردية ثالثة",
        "الرام التجهيز 3 اعادة",
        "الرام التجهيز 3 وردية أولى",
        "الرام التجهيز 3 وردية ثانية",
        "الرام التجهيز 3 وردية ثالثة",
        "الرام التجهيز 4 اعادة",
        "الرام التجهيز 4 وردية أولى",
        "الرام التجهيز 4 وردية ثانية",
        "الرام التجهيز 4 وردية ثالثة",

        // ---- Machines ----
        "الفراده",
        "الفراده اعادة",
        "الفراده وردية أولى",
        "الفراده وردية ثانية",
        "الفراده وردية ثالثة",
        "اللف",
        "اللف اعادة",
        "اللف وردية أولى",
        "اللف وردية ثانية",
        "اللف وردية ثالثة",
        "انكماش وتكسير",
        "بيتش",
        "بيتش اعادة",
        "بيتش وردية أولى",
        "بيتش وردية ثانية",
        "بيتش وردية ثالثة",
        "روتاري",
        "روتاري اعادة",
        "روتاري وردية أولى",
        "روتاري وردية ثانية",
        "روتاري وردية ثالثة",
        "سوفلينا",
        "سوفلينا اعادة",
        "سوفلينا وردية أولى",
        "سوفلينا وردية ثانية",
        "سوفلينا وردية ثالثة",
        "عصارة مفتوح",
        "عصارة مفتوح اعادة",
        "عصارة مفتوح وردية أولى",
        "عصارة مفتوح وردية ثانية",
        "عصارة مفتوح وردية ثالثة",
        "عصارة مقفول",
        "عصارة مقفول اعادة",
        "عصارة مقفول وردية أولى",
        "عصارة مقفول وردية ثانية",
        "عصارة مقفول وردية ثالثة",
        "غسيل سوفلينا",
        "كسترا",
        "كسترا اعادة",
        "كسترا وردية أولى",
        "كسترا وردية ثانية",
        "كسترا وردية ثالثة",
        "كلندر",
        "كلندر اعادة",
        "كلندر وردية أولى",
        "كلندر وردية ثانية",
        "كلندر وردية ثالثة",
        "كومباكتور",
        "كومباكتور اعادة",
        "كومباكتور وردية أولى",
        "كومباكتور وردية ثانية",
        "كومباكتور وردية ثالثة",
        "مثبت حراري",
        "مثبت حراري اعادة",
        "مثبت حراري وردية أولى",
        "مثبت حراري وردية ثانية",
        "مثبت حراري وردية ثالثة",
        "مثبت مائي1 وردية أولى",
        "مثبت مائي1 وردية ثانية",
        "مثبت مائي1 وردية ثالثة",
        "مثبت مائي2 اعادة",
        "مثبت مائي2 وردية أولى",
        "مثبت مائي2 وردية ثانية",
        "مثبت مائي2 وردية ثالثة",
        "مجفف",
        "مجفف اعادة",
        "مجفف وردية أولى",
        "مجفف وردية ثانية",
        "مجفف وردية ثالثة"
    };

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
                    requiresApproval: false,
                    // No skipping: every stage the operator runs must keep its
                    // baseline so the loss chain stays intact.
                    allowSkip: false,
                    allowRepeat: false,
                    // Rework is an explicit selectable "اعادة" stage, so it is not
                    // also modelled as moving a Job Order backwards.
                    allowRework: false,
                    allowReturn: false,
                    notes: null,
                    isFormationStage: name == "التشكيل",
                    isReadyGoodsStage: name == "الجاهز"));

                added++;
            }

            sequence++;
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