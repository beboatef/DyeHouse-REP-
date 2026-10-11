using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DyeHouseERP.Persistence.Seeding;

/// <summary>
/// Seeds the initial MACHINE master data and its stage mapping (S-Dynamic-Machines).
///
/// The factory's current numbered-machine requirement is:
///   الجيت            = 15
///   الرام التثبيت     = 4
///   الرام التجهيز     = 4
///   مثبت مائي        = 2
///   مثبت حراري       = 1
/// and no numbered machine is required for any other operation today.
///
/// This is INITIAL master data, not a rule: machines are added, renamed,
/// activated/deactivated and re-mapped from inside the ERP with no source-code
/// change, and there is no fixed maximum. A machine can serve more than one
/// stage and a stage can have more than one machine.
///
/// Seeding is idempotent by machine Code (MC001..): existing machines are never
/// modified or removed, and a missing stage→machine link is added without
/// touching the others.
/// </summary>
public static class MachineSeeder
{
    /// <summary>
    /// Stage code (from ProductionStageSeeder) → number of machines to attach.
    /// Public so the approved mapping
    /// (الجيت = 15, الرام التثبيت = 4, الرام التجهيز = 4, مثبت مائي = 2, مثبت حراري = 1)
    /// is asserted by unit tests without needing a database.
    /// </summary>
    public static readonly (string StageCode, int MachineCount)[] MachineMap =
    {
        ("PS004", 15), // الجيت
        ("PS005", 4),  // الرام التثبيت
        ("PS006", 4),  // الرام التجهيز
        ("PS020", 2),  // مثبت مائي
        ("PS019", 1)   // مثبت حراري
    };

    public static async Task SeedAsync(
        ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var stages = await db.ProductionStageDefinitions
            .AsNoTracking()
            .Where(s => MachineMap.Select(m => m.StageCode).Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => new { s.Id, s.Name }, cancellationToken);

        if (stages.Count == 0)
            return; // stage seeder has not run yet

        var existingMachines = await db.Machines
            .ToDictionaryAsync(m => m.Code, cancellationToken);

        var existingLinks = db.StageMachines
            .Select(l => new { l.StageDefinitionId, l.MachineId })
            .ToHashSet();

        var machineIndex = 0;
        var machinesAdded = 0;
        var linksAdded = 0;

        foreach (var (stageCode, machineCount) in MachineMap)
        {
            if (!stages.TryGetValue(stageCode, out var stage))
                continue;

            for (var n = 1; n <= machineCount; n++)
            {
                machineIndex++;
                var code = $"MC{machineIndex:000}";

                if (!existingMachines.TryGetValue(code, out var machine))
                {
                    machine = new Machine(code, $"{stage.Name} {n}", createdBy: "seed");
                    db.Machines.Add(machine);
                    existingMachines[code] = machine;
                    machinesAdded++;
                }

                var linkKey = new { StageDefinitionId = stage.Id, MachineId = machine.Id };
                if (existingLinks.Contains(linkKey))
                    continue;

                db.StageMachines.Add(new StageMachine(stage.Id, machine.Id));
                existingLinks.Add(linkKey);
                linksAdded++;
            }
        }

        if (machinesAdded > 0 || linksAdded > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Seeded {Machines} machines and {Links} stage-machine links.", machinesAdded, linksAdded);
        }
    }
}
