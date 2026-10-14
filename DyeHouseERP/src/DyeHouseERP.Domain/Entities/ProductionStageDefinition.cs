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

    // Phase 4: AllowSkip was REMOVED here. It was the last capability flag, read only by the legacy
    // SkipStageCommand, which has been deleted together with its endpoint - nothing may mark an
    // operation Skipped any more. A stage an order does not actually run simply gets no execution:
    // the operator picks whatever operation they really go to next, which is the whole point of a
    // user-chosen route. AllowRepeat/AllowRework/AllowReturn were removed earlier for the same reason
    // (they advertised behaviour no code path honoured).

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

    // Semantic capability, never inferred from a name or sequence number.
    public bool RequiresDrawingNumber { get; private set; }
    public string? Notes { get; private set; }

    private ProductionStageDefinition() { } // EF Core

    public ProductionStageDefinition(
        string code, string name, int sequence, string createdBy,
        bool requiresInputQuantity = true, bool requiresOutputQuantity = true,
        string? notes = null,
        bool isFormationStage = false, bool isReadyGoodsStage = false,
        bool requiresDrawingNumber = false)
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
        IsFormationStage = isFormationStage;
        IsReadyGoodsStage = isReadyGoodsStage;
        RequiresDrawingNumber = requiresDrawingNumber;
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

    /// <summary>
    /// Edits the administrative fields of a stage: its display name, its notes and its
    /// display order.
    ///
    /// The CODE is deliberately not editable - it is the stable identity the starting data
    /// and the machine map refer to, so renaming in the UI can never orphan a row. Nothing
    /// here touches a Production Order either: an existing stage execution keeps its
    /// StageDefinitionId and its whole recorded history, whatever the stage is called
    /// afterwards.
    /// </summary>
    public void UpdateDetails(string name, int sequence, string? notes, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Stage name is required.", nameof(name));
        if (sequence < 1)
            throw new ArgumentException("The stage order must be 1 or greater.", nameof(sequence));

        Name = name.Trim();
        Sequence = sequence;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}
