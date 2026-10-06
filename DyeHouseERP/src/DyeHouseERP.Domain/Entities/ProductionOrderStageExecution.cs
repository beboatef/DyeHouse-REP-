using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// One stage's execution within one Production Order (spec sections 19-22).
/// Input/output/loss/separates are all independently nullable KG/Meter pairs
/// - a stage records only the quantities that are actually relevant to it,
/// never a universal formula forced onto every stage type.
/// </summary>
public class ProductionOrderStageExecution : BaseEntity
{
    public Guid ProductionOrderId { get; private set; }
    public Guid StageDefinitionId { get; private set; }
    public int Sequence { get; private set; } // snapshotted from the stage definition at route-build time

    public StageExecutionStatus Status { get; private set; } = StageExecutionStatus.Pending;

    public decimal? InputKg { get; private set; }
    public decimal? InputMeter { get; private set; }
    public decimal? OutputKg { get; private set; }
    public decimal? OutputMeter { get; private set; }
    public decimal? LossKg { get; private set; }
    public decimal? LossMeter { get; private set; }
    public decimal? SeparatesKg { get; private set; }
    public decimal? SeparatesMeter { get; private set; }

    /// <summary>
    /// The IMMUTABLE weight this stage started from (spec section 15).
    /// For the first stage it is the Job Order's actual weight; for every later
    /// stage it is the previous stage's final output. It is written once when the
    /// stage is activated and is NEVER overwritten - the output may be edited
    /// before transfer, but the baseline the loss is measured against stays put,
    /// so 783 baseline / 775 then 778 edited reads as a real 1.02% then 0.64%.
    /// </summary>
    public decimal? BaselineKg { get; private set; }
    public decimal? BaselineMeter { get; private set; }

    /// <summary>
    /// Loss as a percentage of the baseline, recorded when the stage is closed.
    /// Null while the stage is still open, because there is no final figure yet.
    /// </summary>
    public decimal? LossPercentKg { get; private set; }
    public decimal? LossPercentMeter { get; private set; }

    public string? Operator { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public bool ApprovalRequired { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. Quantities and
    /// status on a stage feed production output and separates, so two operators
    /// recording the same stage at once must not silently overwrite each other.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ProductionOrderStageExecution() { } // EF Core

    internal ProductionOrderStageExecution(Guid productionOrderId, Guid stageDefinitionId, int sequence)
    {
        ProductionOrderId = productionOrderId;
        StageDefinitionId = stageDefinitionId;
        Sequence = sequence;
    }

    /// <summary>
    /// Activates this stage with its starting weight (spec section 14). This is
    /// the ONLY moment a baseline is set, which is what makes it immutable
    /// afterwards: once the stage has run, its baseline is history.
    /// </summary>
    public void Activate(decimal? baselineKg, decimal? baselineMeter, string startedBy)
    {
        if (Status != StageExecutionStatus.Pending)
            throw new DomainException($"Stage is {Status} and cannot be activated.");

        BaselineKg = baselineKg;
        BaselineMeter = baselineMeter;
        Status = StageExecutionStatus.InProgress;
        Operator = startedBy;
        StartedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Closes the stage by recording its final output and deriving the loss from
    /// the immutable baseline (spec sections 14-15). Loss = baseline - output;
    /// loss % = loss / baseline x 100. KG and Meter are handled independently -
    /// they are never converted into one another.
    ///
    /// A completed stage is LOCKED: this throws once the stage is Completed, so
    /// an ordinary re-entry cannot rewrite a finished stage's figures.
    /// </summary>
    public void CloseWithOutput(
        decimal? outputKg, decimal? outputMeter, decimal? separatesKg, decimal? separatesMeter,
        string? notes, string completedBy)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException(
                "This stage is already closed and locked. A finished stage is corrected with a compensating movement, never by re-entering its figures.");

        if (outputKg is null && outputMeter is null)
            throw new DomainException("Enter the final output quantity for this stage.");

        OutputKg = outputKg;
        OutputMeter = outputMeter;
        SeparatesKg = separatesKg;
        SeparatesMeter = separatesMeter;
        Notes = notes;
        Operator = completedBy;

        LossKg = BaselineKg.HasValue && outputKg.HasValue ? BaselineKg - outputKg : null;
        LossMeter = BaselineMeter.HasValue && outputMeter.HasValue ? BaselineMeter - outputMeter : null;

        // Guard against a divide-by-zero on a zero baseline; a zero baseline
        // simply has no meaningful percentage and stays null.
        LossPercentKg = BaselineKg is > 0 && LossKg is not null
            ? Math.Round(LossKg.Value / BaselineKg.Value * 100, 2)
            : null;
        LossPercentMeter = BaselineMeter is > 0 && LossMeter is not null
            ? Math.Round(LossMeter.Value / BaselineMeter.Value * 100, 2)
            : null;

        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Edits the stage's output WHILE it is still open (spec section 16). No
    /// approval is needed for this, but the baseline is untouched and the caller
    /// records the previous/new figures in the audit log. Once the stage is
    /// closed this is refused, which is what makes a completed stage locked.
    /// </summary>
    public void UpdateOutputWhileOpen(decimal? outputKg, decimal? outputMeter)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException(
                "This stage is closed and locked; its output can no longer be edited.");
        if (outputKg is null && outputMeter is null)
            throw new DomainException("Enter the output quantity for this stage.");
        if (outputKg is < 0 || outputMeter is < 0)
            throw new DomainException("A stage output cannot be negative.");

        OutputKg = outputKg;
        OutputMeter = outputMeter;
    }

    public void Start(string operatorName)
    {
        if (Status != StageExecutionStatus.Pending)
            throw new DomainException($"Stage is already {Status} and cannot be started again.");

        Status = StageExecutionStatus.InProgress;
        Operator = operatorName;
        StartedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Records the stage's recorded quantities and marks it complete. Whether
    /// input/output are required is enforced by the caller against the
    /// matching ProductionStageDefinition (RequiresInputQuantity /
    /// RequiresOutputQuantity) - the entity itself stays agnostic about
    /// which stage type it is.
    /// </summary>
    public void Complete(
        decimal? inputKg, decimal? inputMeter, decimal? outputKg, decimal? outputMeter,
        decimal? lossKg, decimal? lossMeter, decimal? separatesKg, decimal? separatesMeter,
        string? notes, bool approvalRequired, string? approvedBy)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException("Stage is already completed.");

        if (approvalRequired && string.IsNullOrWhiteSpace(approvedBy))
            throw new DomainException("This stage requires approval before it can be completed.");

        InputKg = inputKg; InputMeter = inputMeter;
        OutputKg = outputKg; OutputMeter = outputMeter;
        LossKg = lossKg; LossMeter = lossMeter;
        SeparatesKg = separatesKg; SeparatesMeter = separatesMeter;
        Notes = notes;
        ApprovalRequired = approvalRequired;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = approvalRequired ? DateTime.UtcNow : null;

        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Skip(bool stageAllowsSkip, string reason, string modifiedBy)
    {
        if (!stageAllowsSkip)
            throw new DomainException("This stage is not configured to allow being skipped.");

        Status = StageExecutionStatus.Skipped;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Skipped by {modifiedBy}] {reason}" : $"{Notes}\n[Skipped by {modifiedBy}] {reason}";
        CompletedAtUtc = DateTime.UtcNow;
    }
}
