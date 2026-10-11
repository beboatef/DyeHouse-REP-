using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Association between a production stage definition and a machine
/// (final decision S-Dynamic-Machines). A machine may be linked to more than
/// one stage and a stage may have more than one machine.
///
/// This is ordinary master data: the pair is created and removed from the ERP,
/// not from code, so the machine/stage topology can evolve without a release.
/// Execution history references the machine directly, so removing a link never
/// rewrites a completed stage's history.
/// </summary>
public class StageMachine : BaseEntity
{
    public Guid StageDefinitionId { get; private set; }
    public Guid MachineId { get; private set; }

    private StageMachine() { } // EF Core

    public StageMachine(Guid stageDefinitionId, Guid machineId)
    {
        if (stageDefinitionId == Guid.Empty)
            throw new ArgumentException("A stage-machine link requires a stage.", nameof(stageDefinitionId));
        if (machineId == Guid.Empty)
            throw new ArgumentException("A stage-machine link requires a machine.", nameof(machineId));

        StageDefinitionId = stageDefinitionId;
        MachineId = machineId;
    }
}
