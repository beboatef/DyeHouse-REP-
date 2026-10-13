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

    /// <summary>
    /// The machine this stage execution ran on, and the execution shift it ran in
    /// (S-Dynamic-Machines / S-Production-Execution-Shifts).
    ///
    /// These are HISTORICAL references, kept on the execution itself rather than derived
    /// from the stage's current machine mapping: a completed stage must still name the
    /// machine and shift it actually ran on even after that machine is re-mapped,
    /// deactivated or removed from the stage, so the production history is never rewritten.
    /// Optional because a stage may legitimately run without either being recorded.
    /// </summary>
    public Guid? MachineId { get; private set; }
    public Guid? ProductionShiftId { get; private set; }

    public bool RequiresDrawingNumber { get; private set; }
    public string? DrawingNumber { get; private set; }

    /// <summary>
    /// Sets - or CORRECTS - this execution's drawing number.
    ///
    /// A print run that has already been CLOSED may still be corrected, but only as an explicit
    /// correction carrying a reason: the drawing number is documentary data (delivery statements
    /// read it live from the executions), so a typo must be fixable, while a silent overwrite of the
    /// drawing a run actually produced must not be possible. The caller records the old and new value,
    /// the user, the time and the reason in the audit trail (UpdateStageDrawingCommand).
    /// A drawn number is never invented or inherited here, a skipped run keeps its own, and a stage
    /// that does not require a drawing number refuses one outright.
    /// </summary>
    public void SetDrawingNumber(string drawingNumber, string? correctionReason = null)
    {
        if (!RequiresDrawingNumber)
            throw new DomainException("This stage does not require a drawing number.");
        if (Status == StageExecutionStatus.Skipped)
            throw new DomainException("A skipped stage's drawing number cannot be changed.");
        if (Status == StageExecutionStatus.Completed && string.IsNullOrWhiteSpace(correctionReason))
            throw new DomainException(
                "Correcting the drawing number of a closed stage requires a reason - it is recorded in the audit trail. تصحيح رقم رسمة مرحلة منتهية يتطلب سببًا.");
        if (string.IsNullOrWhiteSpace(drawingNumber) || drawingNumber.Trim().Length > 100)
            throw new DomainException("Drawing number is required and must not exceed 100 characters. رقم الرسمة مطلوب بحد أقصى 100 حرف.");
        DrawingNumber = drawingNumber.Trim();
    }

    public void EnsureDrawingNumber()
    {
        if (RequiresDrawingNumber && string.IsNullOrWhiteSpace(DrawingNumber))
            throw new DomainException("Drawing number is required before printing. رقم الرسمة مطلوب قبل تنفيذ الطباعة.");
    }

    public string? Operator { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. Quantities and
    /// status on a stage feed production output and separates, so two operators
    /// recording the same stage at once must not silently overwrite each other.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ProductionOrderStageExecution() { } // EF Core

    internal ProductionOrderStageExecution(Guid productionOrderId, Guid stageDefinitionId, int sequence, bool requiresDrawingNumber = false, string? drawingNumber = null)
    {
        RequiresDrawingNumber = requiresDrawingNumber;
        if (drawingNumber is not null) SetDrawingNumber(drawingNumber);
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

        EnsureDrawingNumber();
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

        EnsureDrawingNumber();
        if (outputKg is < 0 || outputMeter is < 0 || separatesKg is < 0 || separatesMeter is < 0)
            throw new DomainException("Stage quantities cannot be negative.");
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

    /// <summary>
    /// Guards the ready-goods transfers linked to this stage (spec section 23): the
    /// quantities posted against a stage may never together exceed the stage's own
    /// recorded output, and a unit the stage never recorded can never be transferred.
    /// KG and Meter are checked independently - filling one says nothing about the other.
    /// </summary>
    public void EnsureTransfersWithinOutput(
        decimal alreadyTransferredKg, decimal alreadyTransferredMeter,
        decimal? transferKg, decimal? transferMeter)
    {
        if (Status != StageExecutionStatus.Completed || (OutputKg is null && OutputMeter is null))
            throw new DomainException(
                "This stage has not been closed, so it has no output to transfer to ready goods.");

        if (transferKg is > 0 && OutputKg is null)
            throw new DomainException(
                "This stage recorded no KG output, so KG cannot be transferred against it.");
        if (transferMeter is > 0 && OutputMeter is null)
            throw new DomainException(
                "This stage recorded no Meter output, so Meter cannot be transferred against it.");

        if (transferKg is > 0 && alreadyTransferredKg + transferKg.Value > OutputKg!.Value + 0.001m)
            throw new DomainException(
                $"Cumulative transfers can never exceed a stage's output: this stage produced {OutputKg} KG, {alreadyTransferredKg} KG has already been transferred and {transferKg} KG more was requested.");
        if (transferMeter is > 0 && alreadyTransferredMeter + transferMeter.Value > OutputMeter!.Value + 0.001m)
            throw new DomainException(
                $"Cumulative transfers can never exceed a stage's output: this stage produced {OutputMeter} Meter, {alreadyTransferredMeter} Meter has already been transferred and {transferMeter} Meter more was requested.");
    }

    public void Start(string operatorName)
    {
        if (Status != StageExecutionStatus.Pending)
            throw new DomainException($"Stage is already {Status} and cannot be started again.");

        EnsureDrawingNumber();
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
        string? notes)
    {
        if (Status == StageExecutionStatus.Completed)
            throw new DomainException("Stage is already completed.");

        EnsureDrawingNumber();
        if (inputKg is < 0 || inputMeter is < 0 || outputKg is < 0 || outputMeter is < 0 ||
            separatesKg is < 0 || separatesMeter is < 0)
            throw new DomainException("Stage quantities cannot be negative.");
        InputKg = inputKg; InputMeter = inputMeter;
        OutputKg = outputKg; OutputMeter = outputMeter;
        LossKg = lossKg; LossMeter = lossMeter;
        SeparatesKg = separatesKg; SeparatesMeter = separatesMeter;
        Notes = notes;

        Status = StageExecutionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Skip(bool stageAllowsSkip, string reason, string modifiedBy)
    {
        if (!stageAllowsSkip)
            throw new DomainException("This stage is not configured to allow being skipped.");

        if (Status is StageExecutionStatus.Completed or StageExecutionStatus.Skipped)
            throw new DomainException("A closed stage cannot be skipped.");
        if (StartedAtUtc.HasValue) EnsureDrawingNumber();
        Status = StageExecutionStatus.Skipped;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Skipped by {modifiedBy}] {reason}" : $"{Notes}\n[Skipped by {modifiedBy}] {reason}";
        CompletedAtUtc = DateTime.UtcNow;
    }
}
