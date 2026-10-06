using DyeHouseERP.Domain.Common;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Configurable production stage definition (spec section 19). The system
/// deliberately does NOT hard-code stage names like "preparation/dyeing/
/// washing/drying/finishing" - administrators define whatever sequence
/// actually matches the factory's process. A Production Order's stage
/// executions are generated from whichever definitions are Active, in
/// Sequence order, at the moment the order is created.
/// </summary>
public class ProductionStageDefinition : AuditableEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool RequiresInputQuantity { get; private set; }
    public bool RequiresOutputQuantity { get; private set; }
    public bool RequiresApproval { get; private set; }
    public bool AllowSkip { get; private set; }
    public bool AllowRepeat { get; private set; }
    public bool AllowRework { get; private set; }
    public bool AllowReturn { get; private set; }

    /// <summary>
    /// Marks the stage that every new Job Order starts at - التشكيل.
    ///
    /// This is an EXPLICIT flag rather than "whichever stage has the lowest
    /// Sequence": sequence is an ordering concern that an admin is free to
    /// rearrange, and inferring the formation stage from it would silently move
    /// every Job Order's starting point the moment a stage was reordered. At
    /// most one stage may carry this flag (enforced by the command handlers).
    /// </summary>
    public bool IsFormationStage { get; private set; }

    /// <summary>
    /// Marks the FINAL stage - الجاهز (Ready Goods).
    ///
    /// Explicit for the same reason as <see cref="IsFormationStage"/>: it is never
    /// inferred from Sequence. Picking "الجاهز" at the end of a Job Order transfers
    /// the output to Ready Goods and completes the order (spec section 17), so
    /// getting this wrong would move a job to the wrong warehouse.
    ///
    /// Two invariants keep a route unambiguous, enforced in the domain rather than
    /// only in the command handlers so no caller can bypass them:
    ///   * at most ONE stage may be the Ready Goods stage;
    ///   * a stage may not be BOTH the formation stage and the Ready Goods stage.
    /// </summary>
    public bool IsReadyGoodsStage { get; private set; }

    public string? Notes { get; private set; }

    private ProductionStageDefinition() { } // EF Core

    public ProductionStageDefinition(
        string code, string name, int sequence, string createdBy,
        bool requiresInputQuantity = true, bool requiresOutputQuantity = true,
        bool requiresApproval = false, bool allowSkip = false, bool allowRepeat = false,
        bool allowRework = true, bool allowReturn = false, string? notes = null,
        bool isFormationStage = false, bool isReadyGoodsStage = false)
    {
        if (isFormationStage && isReadyGoodsStage)
            throw new ArgumentException(
                "A stage cannot be both the formation stage and the ready-goods stage: every Job Order must start at one and finish at the other.",
                nameof(isReadyGoodsStage));

        Code = code.Trim();
        Name = name.Trim();
        Sequence = sequence;
        RequiresInputQuantity = requiresInputQuantity;
        RequiresOutputQuantity = requiresOutputQuantity;
        RequiresApproval = requiresApproval;
        AllowSkip = allowSkip;
        AllowRepeat = allowRepeat;
        AllowRework = allowRework;
        AllowReturn = allowReturn;
        IsFormationStage = isFormationStage;
        IsReadyGoodsStage = isReadyGoodsStage;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Marks or unmarks this stage as the formation stage (التشكيل). Uniqueness across stages is enforced by the caller.</summary>
    public void SetFormationStage(bool isFormationStage, string modifiedBy)
    {
        if (isFormationStage && IsReadyGoodsStage)
            throw new ArgumentException(
                "This stage is already the ready-goods stage; a stage cannot be both the first and the last stop of a route.",
                nameof(isFormationStage));

        IsFormationStage = isFormationStage;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks or unmarks this stage as the ready-goods stage (الجاهز). Uniqueness
    /// across stages is enforced by the caller, which clears the flag elsewhere.
    /// </summary>
    public void SetReadyGoodsStage(bool isReadyGoodsStage, string modifiedBy)
    {
        if (isReadyGoodsStage && IsFormationStage)
            throw new ArgumentException(
                "This stage is already the formation stage; a stage cannot be both the first and the last stop of a route.",
                nameof(isReadyGoodsStage));

        IsReadyGoodsStage = isReadyGoodsStage;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string modifiedBy) { IsActive = false; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }
    public void Activate(string modifiedBy) { IsActive = true; ModifiedBy = modifiedBy; ModifiedAtUtc = DateTime.UtcNow; }

    public void Reorder(int sequence, string modifiedBy)
    {
        Sequence = sequence;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
